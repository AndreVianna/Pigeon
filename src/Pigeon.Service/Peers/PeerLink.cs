using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Runtime.CompilerServices;
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
// Reason, from the listing machine, only selects between fixed texts ("wrong-codes"); it is never shown as received.
internal sealed record JoinStatusResponse(string State, string? Tool, string? Reason = null);
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

    // TLS 1.3, or 1.2 where one side lacks it: .NET on macOS accepts no TLS 1.3 connections (found
    // in CI on 2026-11-07), so a Mac could not listen. Both versions require both certificates, and
    // the pinned fingerprints are the trust either way; under 1.2 the certificates cross the network
    // unencrypted, so an observer can tell which machines talk, but not read or change what they say.
    // Allowed by the user on 2026-11-07.
    internal const SslProtocols PeerProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    internal string Fingerprint => identity.Fingerprint;

    public async Task RunAsync(CancellationToken cancellation) {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => {
            options.Limits.MaxRequestBodySize = 1048576;
            options.Limits.MaxConcurrentConnections = 32;
            foreach (var endpoint in endpoints)
                options.Listen(endpoint, listen => listen.UseHttps(https => {
                    https.ServerCertificate = identity.Certificate;
                    https.SslProtocols = PeerProtocols;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    // Self-signed certificates have no chain to check; the fingerprint is the trust.
                    https.ClientCertificateValidation = (certificate, _, _) => Admits(ServiceIdentity.FingerprintOf(certificate));
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
                        if (JoinStart(fingerprint, ip, address, await context.Request.ReadFromJsonAsync<JoinStartRequest>(cancellationToken: context.RequestAborted)) is not { } answer) {
                            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                            return;
                        }

                        await context.Response.WriteAsJsonAsync(answer, cancellationToken: context.RequestAborted);
                        return;
                    }
                    case JoinRevealPath:
                        context.Response.StatusCode = JoinReveal(fingerprint, await context.Request.ReadFromJsonAsync<JoinRevealRequest>(cancellationToken: context.RequestAborted))
                            ? StatusCodes.Status204NoContent : StatusCodes.Status403Forbidden;
                        return;
                    case JoinStatusPath:
                        await context.Response.WriteAsJsonAsync(JoinStatus(fingerprint, await context.Request.ReadFromJsonAsync<JoinStatusRequest>(cancellationToken: context.RequestAborted)), cancellationToken: context.RequestAborted);
                        return;
                }

                if (!Knows(fingerprint)) {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                if (context.Request.Path == TopicsPath) {
                    await context.Response.WriteAsJsonAsync(Topics(), cancellationToken: context.RequestAborted);
                    return;
                }

                if (context.Request.Path != DeliverPath) {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                if (Deliver(fingerprint, address, await context.Request.ReadFromJsonAsync<DeliverRequest>(cancellationToken: context.RequestAborted)) is not { } results) {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await context.Response.WriteAsJsonAsync(results, cancellationToken: context.RequestAborted);
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

    // ---- The protocol, apart from HTTPS ----
    // Each request as the listener has it once the handshake is done: from the machine with this
    // fingerprint, at this IP address and, when it named its listener port, at this address.

    // The handshake admits known machines, and any machine while a topic is listed or a join is open.
    internal bool Admits(string fingerprint) => broker.IsPeer(fingerprint) || broker.AnyListed();

    // Past the join paths, only known machines are served.
    internal bool Knows(string fingerprint) => broker.IsPeer(fingerprint);

    // Null when the request is refused for its form or for too many requests.
    internal JoinStartResponse? JoinStart(string fingerprint, IPAddress ip, string? address, JoinStartRequest? start) {
        var answer = start is null || address is null || !rates.Allow("join:" + ip, 1, 10) || !rates.Allow("join", 1, 60)
            ? null : broker.JoinStart(fingerprint, address, start.Topic, start.Name, start.Tool, start.Commitment);
        return answer is { } a ? new JoinStartResponse(a.Id, Convert.ToBase64String(a.Nonce), a.Name, a.NeedsCode) : null;
    }

    internal bool JoinReveal(string fingerprint, JoinRevealRequest? reveal)
        => reveal is not null && broker.JoinReveal(identity.Fingerprint, fingerprint, reveal.Request, Convert.FromBase64String(reveal.Nonce));

    internal JoinStatusResponse JoinStatus(string fingerprint, JoinStatusRequest? status) {
        var (state, tool, reason) = status is null ? ("denied", null, null) : broker.JoinStatus(fingerprint, status.Request);
        return new JoinStatusResponse(state, tool, reason);
    }

    internal TopicsResponse Topics() => new(broker.ListedTopics());

    // From a known machine; null when the request holds no items, or too many.
    internal DeliverResponse? Deliver(string fingerprint, string? address, DeliverRequest? request) {
        if (request?.Items is not { Length: > 0 and <= MaxItemsPerRequest } items)
            return null;
        return new DeliverResponse(rates.Allow(fingerprint, items.Length) ? broker.FromPeer(fingerprint, address, items) : [.. items.Select(_ => "limited")]);
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

// What this machine asks of another one, in the peer protocol's terms. HttpsPeerLink is the real
// one; the unit tests connect two services in memory. A machine that cannot be reached, fails the
// handshake or fails a request throws HttpRequestException; one that answers at the address with
// another identity than the pinned one throws PeerIdentityException.
internal interface IPeerLink {
    // The first request of a join. With no expected fingerprint (a first contact) any machine may
    // answer; the result names the machine that did, also when it is not the expected one.
    Task<JoinStarted> StartJoinAsync(string address, string? expectedFingerprint, JoinStartRequest start, CancellationToken cancellation);
    // Whether the machine accepted the revealed value of the join code exchange.
    Task<bool> RevealJoinAsync(string address, string fingerprint, JoinRevealRequest reveal, CancellationToken cancellation);
    Task<JoinStatusResponse> JoinStatusAsync(string address, string fingerprint, JoinStatusRequest status, CancellationToken cancellation);
    Task<string[]> TopicsAsync(string address, string fingerprint, CancellationToken cancellation);
    // The outcome of each item, or null when the machine gave none.
    Task<string[]?> DeliverAsync(string address, string fingerprint, PeerItem[] items, CancellationToken cancellation);
}

// The machine at a known machine's address presented another identity than the pinned one: it was
// reinstalled, or another machine answers there. Nothing was sent to it.
internal sealed class PeerIdentityException(string presented) : HttpRequestException("The machine answered with another identity than the pinned one.") {
    public string Presented { get; } = presented;
}

// Which machine answered the start of a join (null if none presented itself), and how: Refused, for
// too many requests when Busy, or with an answer, which may be missing.
internal sealed record JoinStarted(string? Fingerprint, JoinStartResponse? Answer, bool Refused = false, bool Busy = false);

// The peer protocol over HTTPS: mutual TLS with this service's certificate, pinning the other
// machine's fingerprint, and this service's listener port in a header.
internal sealed class HttpsPeerLink(Func<ServiceIdentity> identity, Func<int> listenPort) : IPeerLink, IDisposable {
    // Connects with this service's certificate and accepts only the expected machine's certificate,
    // or, with no expected fingerprint (a first contact), any certificate, which it reports.
    public static HttpClient ClientFor(ServiceIdentity identity, string? expectedFingerprint, Action<string>? presented = null, int port = 0) {
        var client = new HttpClient(new SocketsHttpHandler {
            SslOptions = new SslClientAuthenticationOptions {
                EnabledSslProtocols = PeerServer.PeerProtocols,
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

    // Delivery keeps a client per machine, with the identity its last handshake presented; the other
    // requests use one each.
    private readonly Dictionary<string, (HttpClient Client, StrongBox<string?> Presented)> clients = new(StringComparer.Ordinal);

    // A failed handshake in which the machine presented another identity than the pinned one.
    private static async Task<T> PinnedAsync<T>(string fingerprint, Func<string?> presented, Func<Task<T>> request) {
        try {
            return await request();
        }
        catch (HttpRequestException e) when (e is not PeerIdentityException && presented() is { } other && other != fingerprint) {
            throw new PeerIdentityException(other);
        }
    }

    public async Task<JoinStarted> StartJoinAsync(string address, string? expectedFingerprint, JoinStartRequest start, CancellationToken cancellation) {
        string? presented = null;
        using var client = ClientFor(identity(), expectedFingerprint, f => presented = f, listenPort());
        HttpResponseMessage response;
        try {
            response = await client.PostAsJsonAsync($"https://{address}{PeerServer.JoinStartPath}", start, cancellation);
        }
        catch (HttpRequestException) when (expectedFingerprint is not null && presented is not null && presented != expectedFingerprint) {
            return new JoinStarted(presented, null, Refused: true);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
            return new JoinStarted(presented, null, Refused: true, Busy: response.StatusCode == HttpStatusCode.TooManyRequests);
        return new JoinStarted(presented, await response.Content.ReadFromJsonAsync<JoinStartResponse>(cancellation));
    }

    public async Task<bool> RevealJoinAsync(string address, string fingerprint, JoinRevealRequest reveal, CancellationToken cancellation) {
        string? presented = null;
        using var pinned = ClientFor(identity(), fingerprint, f => presented = f, listenPort());
        return await PinnedAsync(fingerprint, () => presented, async () => {
            using var revealed = await pinned.PostAsJsonAsync($"https://{address}{PeerServer.JoinRevealPath}", reveal, cancellation);
            return revealed.IsSuccessStatusCode;
        });
    }

    public async Task<JoinStatusResponse> JoinStatusAsync(string address, string fingerprint, JoinStatusRequest status, CancellationToken cancellation) {
        string? presented = null;
        using var client = ClientFor(identity(), fingerprint, f => presented = f, listenPort());
        return await PinnedAsync(fingerprint, () => presented, async () => {
            using var response = await client.PostAsJsonAsync($"https://{address}{PeerServer.JoinStatusPath}", status, cancellation);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JoinStatusResponse>(cancellation) ?? throw new JsonException();
        });
    }

    public async Task<string[]> TopicsAsync(string address, string fingerprint, CancellationToken cancellation) {
        string? presented = null;
        using var client = ClientFor(identity(), fingerprint, f => presented = f, listenPort());
        return await PinnedAsync(fingerprint, () => presented, async () => {
            using var response = await client.PostAsJsonAsync($"https://{address}{PeerServer.TopicsPath}", new { }, cancellation);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TopicsResponse>(cancellation))?.Topics ?? [];
        });
    }

    public async Task<string[]?> DeliverAsync(string address, string fingerprint, PeerItem[] items, CancellationToken cancellation) {
        if (!clients.TryGetValue(fingerprint, out var pinned)) {
            var box = new StrongBox<string?>();
            clients[fingerprint] = pinned = (ClientFor(identity(), fingerprint, f => box.Value = f, listenPort()), box);
        }

        pinned.Presented.Value = null;
        return await PinnedAsync(fingerprint, () => pinned.Presented.Value, async () => {
            using var response = await pinned.Client.PostAsJsonAsync($"https://{address}{PeerServer.DeliverPath}", new DeliverRequest(items), cancellation);
            return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<DeliverResponse>(cancellation))?.Results : null;
        });
    }

    public void Dispose() {
        foreach (var (client, _) in clients.Values)
            client.Dispose();
    }
}

// Sends due outbox items to each known machine.
internal sealed class PeerDelivery(Broker broker, IPeerLink link) {
    // One pass over every machine with due items; returns how many items were delivered.
    public async Task<int> TickAsync(CancellationToken cancellation) {
        var delivered = 0;
        foreach (var (peer, items) in broker.DueForPeers()) {
            string[]? results = null;
            try {
                var now = broker.Now();
                var payload = items.Select(i => new PeerItem(i.Kind, i.Offered, i.MessageId, i.SenderTool, i.Body, Math.Max(0, i.ExpiresUnixMs - now))).ToArray();
                results = await link.DeliverAsync(peer.Address!, peer.Fingerprint, payload, cancellation);
                broker.PeerAnswered(peer.Fingerprint);
            }
            catch (PeerIdentityException e) {
                _ = broker.PeerIdentityChanged(peer.Fingerprint, e.Presented);
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
}

// The requesting side of a join: contacts the listing machine and fixes the code with it.
internal static class JoinClient {
    public sealed record Contact(string Fingerprint, string Name, string Request, string Code, bool NeedsCode);

    public static async Task<Contact> StartAsync(IPeerLink link, string myFingerprint, string address, string? expectedFingerprint, string topic, string myName, string tool, int myPort, byte[] nonce, CancellationToken cancellation) {
        JoinStarted started;
        try {
            started = await link.StartJoinAsync(address, expectedFingerprint, new JoinStartRequest(topic, myName, tool, myPort, JoinCode.Commit(nonce)), cancellation);
        }
        catch (HttpRequestException e) when (e is not PeerIdentityException) {
            throw new InvalidOperationException($"{address} could not be reached, or no Pigeon there accepts requests (it may have no topic open to other machines, or run an older Pigeon).", e);
        }

        if (expectedFingerprint is not null && started.Fingerprint is { } other && other != expectedFingerprint)
            throw new PeerIdentityException(other);
        if (started.Refused || started.Fingerprint is not { } presented)
            throw new InvalidOperationException(started.Busy ? $"{address} is receiving too many requests; try again in a minute." : $"{address} refused the request.");
        var start = started.Answer ?? throw new InvalidOperationException($"{address} sent no answer.");
        if (!await link.RevealJoinAsync(address, presented, new JoinRevealRequest(start.Request, Convert.ToBase64String(nonce)), cancellation))
            throw new InvalidOperationException($"{address} did not accept the join code exchange.");
        var code = JoinCode.Derive(nonce, Convert.FromBase64String(start.Nonce), presented, myFingerprint, topic);
        return new Contact(presented, start.Name, start.Request, code, start.NeedsCode);
    }
}
