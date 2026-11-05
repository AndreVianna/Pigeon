using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;

namespace Pigeon.Service.Peers;

internal sealed record DeliverRequest(PeerItem[] Items);
internal sealed record DeliverResponse(string[] Results);
internal sealed record JoinStartRequest(string Topic, string Name, string? Tool, int Port, string Commitment);
internal sealed record JoinStartResponse(string Request, string Nonce, string Name, bool NeedsCode);
internal sealed record JoinRevealRequest(string Request, string Nonce);
internal sealed record JoinStatusRequest(string Request);
internal sealed record JoinStatusResponse(string State, string? Tool);
internal sealed record TopicsResponse(string[] Topics);

// The peer listener (see docs/remote-design.md): a separate HTTPS endpoint, off until a person turns
// it on, that accepts only TLS 1.3 connections with a client certificate and serves only the peer
// protocol, never the local API. Known machines deliver messages and list topics; any certificate
// reaches only the join paths, and only while a topic is listed here.
public sealed class PeerServer(Broker broker, ServiceIdentity identity, IPEndPoint[] endpoints) {
    public const string Version = "/peer/v2";
    public const string DeliverPath = Version + "/deliver", TopicsPath = Version + "/topics";
    public const string JoinStartPath = Version + "/join/start", JoinRevealPath = Version + "/join/reveal", JoinStatusPath = Version + "/join/status";
    // The sender's peer listener port, so the receiver can reach it at the address it connected from.
    public const string PortHeader = "X-Pigeon-Port";
    private const int MaxItemsPerRequest = 50;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly PeerRateLimit rates = new();
    public Task Ready => ready.Task;

    public async Task RunAsync(CancellationToken cancellation) {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = 1048576;
            options.Limits.MaxConcurrentConnections = 32;
            foreach (var endpoint in endpoints)
                options.Listen(endpoint, listen => listen.UseHttps(https => {
                    https.ServerCertificate = identity.Certificate;
                    https.SslProtocols = SslProtocols.Tls13;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    // Self-signed certificates have no chain to check; the fingerprint is the trust.
                    https.ClientCertificateValidation = (certificate, _, _) => broker.IsPeer(ServiceIdentity.FingerprintOf(certificate)) || broker.AnyListed();
                }));
        });
        await using var app = builder.Build();
        app.Run(async context => {
            var certificate = context.Connection.ClientCertificate;
            var fingerprint = certificate is null ? null : ServiceIdentity.FingerprintOf(certificate);
            var ip = context.Connection.RemoteIpAddress is { } remote ? remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote : null;
            if (fingerprint is null || ip is null || context.Request.Method != "POST" || !context.Request.HasJsonContentType()) {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var port = int.TryParse(context.Request.Headers[PortHeader], out var p) && p is > 0 and <= 65535 ? p : 0;
            var address = port == 0 ? null : new IPEndPoint(ip, port).ToString();
            try {
                switch (context.Request.Path.Value) {
                    case JoinStartPath: {
                        var start = await context.Request.ReadFromJsonAsync<JoinStartRequest>(cancellationToken: context.RequestAborted);
                        var answer = start is null || address is null || !rates.Allow("join:" + ip, 1, 10) || !rates.Allow("join", 1, 60)
                            ? null : broker.JoinStart(fingerprint, address, start.Topic, start.Name, start.Tool, start.Commitment);
                        if (answer is not { } a) {
                            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                            return;
                        }

                        await context.Response.WriteAsJsonAsync(new JoinStartResponse(a.Id, Convert.ToBase64String(a.Nonce), a.Name, a.NeedsCode), cancellationToken: context.RequestAborted);
                        return;
                    }
                    case JoinRevealPath: {
                        var reveal = await context.Request.ReadFromJsonAsync<JoinRevealRequest>(cancellationToken: context.RequestAborted);
                        context.Response.StatusCode = reveal is not null && broker.JoinReveal(identity.Fingerprint, fingerprint, reveal.Request, Convert.FromBase64String(reveal.Nonce))
                            ? StatusCodes.Status204NoContent : StatusCodes.Status403Forbidden;
                        return;
                    }
                    case JoinStatusPath: {
                        var status = await context.Request.ReadFromJsonAsync<JoinStatusRequest>(cancellationToken: context.RequestAborted);
                        var (state, tool) = status is null ? ("denied", null) : broker.JoinStatus(fingerprint, status.Request);
                        await context.Response.WriteAsJsonAsync(new JoinStatusResponse(state, tool), cancellationToken: context.RequestAborted);
                        return;
                    }
                }

                if (!broker.IsPeer(fingerprint)) {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                if (context.Request.Path == TopicsPath) {
                    await context.Response.WriteAsJsonAsync(new TopicsResponse(broker.ListedTopics()), cancellationToken: context.RequestAborted);
                    return;
                }

                if (context.Request.Path != DeliverPath) {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var request = await context.Request.ReadFromJsonAsync<DeliverRequest>(cancellationToken: context.RequestAborted);
                if (request?.Items is not { Length: > 0 and <= MaxItemsPerRequest } items) {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var results = rates.Allow(fingerprint, items.Length) ? broker.FromPeer(fingerprint, address, items) : [.. items.Select(_ => "limited")];
                await context.Response.WriteAsJsonAsync(new DeliverResponse(results), cancellationToken: context.RequestAborted);
            }
            catch (Exception e) when (e is JsonException or FormatException or ArgumentException) {
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

// At most this many items per key per minute, so a hostile machine cannot flood the database.
internal sealed class PeerRateLimit {
    private readonly Dictionary<string, (long Window, int Count)> counts = new(StringComparer.Ordinal);

    public bool Allow(string key, int items, int perMinute = 300) {
        lock (counts) {
            var window = Environment.TickCount64 / 60000;
            var (seen, count) = counts.GetValueOrDefault(key);
            if (seen != window)
                count = 0;
            if (count + items > perMinute)
                return false;
            if (counts.Count > 4096)
                counts.Clear();
            counts[key] = (window, count + items);
            return true;
        }
    }
}

// Sends due outbox items to each known machine over mutual TLS, pinning its fingerprint.
public sealed class PeerDelivery(Broker broker, ServiceIdentity identity, Func<int> listenPort) {
    // Connects with this service's certificate and accepts only the expected machine's certificate,
    // or, with no expected fingerprint (a first contact), any certificate, which it reports.
    public static HttpClient ClientFor(ServiceIdentity identity, string? expectedFingerprint, Action<string>? presented = null, int port = 0) {
        var client = new HttpClient(new SocketsHttpHandler {
            SslOptions = new SslClientAuthenticationOptions {
                EnabledSslProtocols = SslProtocols.Tls13,
                ClientCertificates = [identity.Certificate],
                RemoteCertificateValidationCallback = (_, certificate, _, _) => {
                    if (certificate is not X509Certificate2 server)
                        return false;
                    var fingerprint = ServiceIdentity.FingerprintOf(server);
                    presented?.Invoke(fingerprint);
                    return expectedFingerprint is null || fingerprint == expectedFingerprint;
                }
            },
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = TimeSpan.FromSeconds(15) };
        if (port > 0)
            client.DefaultRequestHeaders.Add(PeerServer.PortHeader, port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
    }

    private readonly Dictionary<string, HttpClient> clients = new(StringComparer.Ordinal);

    // One pass over every machine with due items; returns how many items were delivered.
    public async Task<int> TickAsync(CancellationToken cancellation) {
        var delivered = 0;
        foreach (var (peer, items) in broker.DueForPeers()) {
            if (!clients.TryGetValue(peer.Fingerprint, out var client))
                clients[peer.Fingerprint] = client = ClientFor(identity, peer.Fingerprint, port: listenPort());
            string[]? results = null;
            try {
                var now = broker.Now();
                var payload = items.Select(i => new PeerItem(i.Kind, i.Offered, i.MessageId, i.SenderTool, i.Body, Math.Max(0, i.ExpiresUnixMs - now))).ToArray();
                using var response = await client.PostAsJsonAsync($"https://{peer.Address}{PeerServer.DeliverPath}", new DeliverRequest(payload), cancellation);
                if (response.IsSuccessStatusCode)
                    results = (await response.Content.ReadFromJsonAsync<DeliverResponse>(cancellation))?.Results;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException && !cancellation.IsCancellationRequested) { }

            for (var i = 0; i < items.Length; i++) {
                var outcome = results is not null && i < results.Length ? results[i] : null;
                broker.PeerOutcome(peer.Fingerprint, items[i].Id, outcome);
                if (outcome == "ok")
                    delivered++;
            }
        }

        return delivered;
    }

    public void Dispose() {
        foreach (var client in clients.Values)
            client.Dispose();
    }
}

// The requesting side of a join: contacts the listing machine, fixes the code with it, and asks
// whether its person allowed the join.
public static class JoinClient {
    public sealed record Contact(string Fingerprint, string Name, string Request, string Code, bool NeedsCode);

    public static async Task<Contact> StartAsync(ServiceIdentity identity, string address, string? expectedFingerprint, string topic, string myName, string tool, int myPort, byte[] nonce, CancellationToken cancellation) {
        string? presented = null;
        using var client = PeerDelivery.ClientFor(identity, expectedFingerprint, f => presented = f, myPort);
        HttpResponseMessage response;
        try {
            response = await client.PostAsJsonAsync($"https://{address}{PeerServer.JoinStartPath}", new JoinStartRequest(topic, myName, tool, myPort, JoinCode.Commit(nonce)), cancellation);
        }
        catch (HttpRequestException e) when (expectedFingerprint is not null && presented is not null && presented != expectedFingerprint) {
            throw new InvalidOperationException($"the machine at {address} has a different identity than the one you joined before. If it was reinstalled, remove it with pigeon peer remove <name> and join it again by address.", e);
        }
        catch (HttpRequestException e) {
            throw new InvalidOperationException($"{address} could not be reached, or no Pigeon there accepts requests (it may have no topic open to other machines, or run an older Pigeon).", e);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode || presented is null)
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.TooManyRequests ? $"{address} is receiving too many requests; try again in a minute." : $"{address} refused the request.");
        var start = await response.Content.ReadFromJsonAsync<JoinStartResponse>(cancellation) ?? throw new InvalidOperationException($"{address} sent no answer.");
        using var pinned = PeerDelivery.ClientFor(identity, presented, port: myPort);
        using var revealed = await pinned.PostAsJsonAsync($"https://{address}{PeerServer.JoinRevealPath}", new JoinRevealRequest(start.Request, Convert.ToBase64String(nonce)), cancellation);
        if (!revealed.IsSuccessStatusCode)
            throw new InvalidOperationException($"{address} did not accept the join code exchange.");
        var code = JoinCode.Derive(nonce, Convert.FromBase64String(start.Nonce), presented, identity.Fingerprint, topic);
        return new Contact(presented, start.Name, start.Request, code, start.NeedsCode);
    }

    public static async Task<(string State, string? Tool)> StatusAsync(ServiceIdentity identity, string address, string fingerprint, string request, int myPort, CancellationToken cancellation) {
        using var client = PeerDelivery.ClientFor(identity, fingerprint, port: myPort);
        using var response = await client.PostAsJsonAsync($"https://{address}{PeerServer.JoinStatusPath}", new JoinStatusRequest(request), cancellation);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<JoinStatusResponse>(cancellation) ?? throw new JsonException();
        return (status.State, status.Tool);
    }

    public static async Task<string[]> TopicsAsync(ServiceIdentity identity, string address, string fingerprint, int myPort, CancellationToken cancellation) {
        using var client = PeerDelivery.ClientFor(identity, fingerprint, port: myPort);
        using var response = await client.PostAsJsonAsync($"https://{address}{PeerServer.TopicsPath}", new { }, cancellation);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TopicsResponse>(cancellation))?.Topics ?? [];
    }
}
