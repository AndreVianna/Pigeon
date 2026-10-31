using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pigeon.Core;

namespace Pigeon.Service;

internal sealed record BrokerRequest(Principal Principal, string Operation, JsonElement Arguments);

// The protocol is local-only, but is not authenticated: all local users can access it.
public sealed class LocalHttpServer(string port, Broker broker, Action? stopRequested = null) {
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready => ready.Task;

    public async Task RunAsync(CancellationToken cancellation) {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => {
            options.Listen(IPAddress.Loopback, int.Parse(port));
            options.Limits.MaxRequestBodySize = 524288;
        });
        await using var app = builder.Build();
        app.Run(async context => {
            // Deny browser-originated requests and DNS-rebinding hostnames. The
            // custom header and JSON content type also prevent simple form posts.
            if (context.Request.Method != "POST" || context.Request.Path != "/broker" ||
                context.Request.Host.Host != "127.0.0.1" || context.Request.Headers.ContainsKey("Origin") ||
                context.Request.Headers["X-Pigeon-Client"] != "1" || !context.Request.HasJsonContentType()) {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            try {
                var request = await context.Request.ReadFromJsonAsync<BrokerRequest>(cancellationToken: context.RequestAborted);
                var stop = request?.Operation == "service_stop" && stopRequested is not null;
                var result = request is null || request.Principal is null || string.IsNullOrWhiteSpace(request.Principal.Project) || string.IsNullOrWhiteSpace(request.Principal.Agent) || request.Operation is null
                    ? new BrokerResult(false, null, "invalid request")
                    : request.Operation == "service_status" ? new BrokerResult(true, new { available = true }, null)
                    : stop ? new BrokerResult(true, new { stopping = true }, null)
                    : broker.Execute(request.Principal, request.Operation, request.Arguments);
                await context.Response.WriteAsJsonAsync(result, new JsonSerializerOptions(), cancellationToken: context.RequestAborted);
                if (stop && result.Ok) {
                    await context.Response.CompleteAsync();
                    stopRequested!();
                }
            }
            catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
        });
        await app.StartAsync(cancellation);
        ready.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, cancellation); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        await app.StopAsync();
    }
}

public static class LocalHttpClient {
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
    public static async Task<JsonElement> ExecuteAsync(string port, Principal principal, string operation, object args, CancellationToken cancellation = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/broker");
        request.Headers.Add("X-Pigeon-Client", "1");
        request.Content = JsonContent.Create(new { Principal = principal, Operation = operation, Arguments = args });
        try {
            using var response = await Client.SendAsync(request, cancellation);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            return json.RootElement.Clone();
        }
        catch (HttpRequestException e) { throw new IOException("Pigeon service is unavailable", e); }
    }
}
