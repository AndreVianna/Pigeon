using System.Net;
using System.Reflection;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Pigeon.Core;

namespace Pigeon.Service;

internal sealed record BrokerRequest(string Operation, JsonElement Arguments);

// The protocol is local-only, but is not authenticated: all local users can access it.
public sealed class LocalHttpServer(string port, Broker broker, Action? stopRequested = null, Peers.PeerHost? peers = null) {
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
                var peerOperation = request?.Operation is { } op && op.StartsWith("peer_", StringComparison.Ordinal);
                var arguments = request is null || request.Arguments.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement : request.Arguments;
                var result = request is null || request.Operation is null
                    ? new BrokerResult(false, null, "invalid request")
                    : peerOperation ? peers is null ? new BrokerResult(false, null, "Peers are not available in this service.") : await peers.ExecuteAsync(request.Operation, arguments)
                    : request.Operation == "service_status" ? new BrokerResult(true, new { available = true, version = PigeonVersion.Current }, null)
                    : stop ? new BrokerResult(true, new { stopping = true }, null)
                    : broker.Execute(request.Operation, request.Arguments.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement : request.Arguments);
                await context.Response.WriteAsJsonAsync(result, new JsonSerializerOptions(), cancellationToken: context.RequestAborted);
                if (stop && result.Ok) {
                    await context.Response.CompleteAsync();
                    stopRequested!();
                }
            }
            catch (JsonException) {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });
        await app.StartAsync(cancellation);
        ready.TrySetResult();
        try {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }

        await app.StopAsync();
    }
}

public static class PigeonVersion {
    // The package version of the running pigeon executable, without build metadata.
    public static readonly string Current = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}

public static class LocalHttpClient {
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
    // For requests the service answers only after reaching another machine.
    private static readonly HttpClient SlowClient = new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(40) };
    public static Task<JsonElement> ExecuteAsync(string port, string operation, object args, CancellationToken cancellation = default) => ExecuteAsync(port, operation, args, slow: false, cancellation);

    public static async Task<JsonElement> ExecuteAsync(string port, string operation, object args, bool slow, CancellationToken cancellation = default) {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/broker");
        request.Headers.Add("X-Pigeon-Client", "1");
        request.Content = JsonContent.Create(new { Operation = operation, Arguments = args });
        try {
            using var response = await (slow ? SlowClient : Client).SendAsync(request, cancellation);
            response.EnsureSuccessStatusCode();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            // Report a temporary storage failure like an outage, so callers retry instead of treating it as a rejection.
            if (json.RootElement.TryGetProperty("Error", out var error) && error.ValueKind == JsonValueKind.String && error.GetString() == Broker.StorageUnavailable)
                throw new IOException("Pigeon storage is temporarily unavailable");
            return json.RootElement.Clone();
        }
        catch (HttpRequestException e) {
            throw new IOException("Pigeon service is unavailable", e);
        }
    }
}
