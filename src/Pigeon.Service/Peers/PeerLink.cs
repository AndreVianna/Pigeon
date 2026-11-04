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

using Pigeon.Core;

namespace Pigeon.Service.Peers;

internal sealed record DeliverRequest(PeerItem[] Items);
internal sealed record DeliverResponse(string[] Results);
internal sealed record PairRequest(string Name, string? Address, string Proof);
internal sealed record PairResponse(string Name, string Check);

// The peer listener (see docs/remote-design.md): a separate HTTPS endpoint, off unless the user names
// an address, that accepts only TLS 1.3 connections whose client certificate belongs to a paired peer,
// and serves only the peer protocol. It never serves the local API.
public sealed class PeerServer(Broker broker, ServiceIdentity identity, IPEndPoint endpoint) {
    public const string DeliverPath = "/peer/v1/deliver";
    public const string PairPath = "/peer/v1/pair";
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
            options.Listen(endpoint, listen => listen.UseHttps(https => {
                https.ServerCertificate = identity.Certificate;
                https.SslProtocols = SslProtocols.Tls13;
                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                // Self-signed certificates have no chain to check; the fingerprint is the trust.
                // An unpaired certificate gets through the handshake only while an invitation is open,
                // and then only to the pairing path.
                https.ClientCertificateValidation = (certificate, _, _) => broker.IsPeer(ServiceIdentity.FingerprintOf(certificate)) || broker.HasOpenInvitation();
            }));
        });
        await using var app = builder.Build();
        app.Run(async context => {
            var certificate = context.Connection.ClientCertificate;
            var fingerprint = certificate is null ? null : ServiceIdentity.FingerprintOf(certificate);
            if (fingerprint is not null && context.Request.Method == "POST" && context.Request.Path == PairPath && context.Request.HasJsonContentType()) {
                try {
                    var pair = await context.Request.ReadFromJsonAsync<PairRequest>(cancellationToken: context.RequestAborted);
                    var check = pair is null || !rates.Allow("pair", 1) ? null : broker.Pair(identity.Fingerprint, fingerprint, pair.Proof, pair.Name, pair.Address);
                    if (check is null) {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return;
                    }

                    await context.Response.WriteAsJsonAsync(new PairResponse(broker.PeerSetting("name") ?? "pigeon", check), cancellationToken: context.RequestAborted);
                }
                catch (Exception e) when (e is JsonException or ArgumentException) {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }

                return;
            }

            if (fingerprint is null || !broker.IsPeer(fingerprint) || context.Request.Method != "POST" || context.Request.Path != DeliverPath || !context.Request.HasJsonContentType()) {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            try {
                var request = await context.Request.ReadFromJsonAsync<DeliverRequest>(cancellationToken: context.RequestAborted);
                if (request?.Items is not { Length: > 0 and <= MaxItemsPerRequest } items) {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var results = rates.Allow(fingerprint, items.Length) ? broker.FromPeer(fingerprint, items) : [.. items.Select(_ => "limited")];
                await context.Response.WriteAsJsonAsync(new DeliverResponse(results), cancellationToken: context.RequestAborted);
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

// At most this many items per peer per minute, so a hostile peer cannot flood the database.
internal sealed class PeerRateLimit(int perMinute = 300) {
    private readonly Dictionary<string, (long Window, int Count)> counts = new(StringComparer.Ordinal);

    public bool Allow(string fingerprint, int items) {
        lock (counts) {
            var window = Environment.TickCount64 / 60000;
            var (seen, count) = counts.GetValueOrDefault(fingerprint);
            if (seen != window)
                count = 0;
            if (count + items > perMinute)
                return false;
            counts[fingerprint] = (window, count + items);
            return true;
        }
    }
}

// Sends due outbox items to each paired peer over mutual TLS, pinning the peer's fingerprint.
public sealed class PeerDelivery(Broker broker, ServiceIdentity identity) {
    // Connects with this service's certificate and accepts only the expected peer's certificate.
    public static HttpClient ClientFor(ServiceIdentity identity, string expectedFingerprint) => new(new SocketsHttpHandler {
        SslOptions = new SslClientAuthenticationOptions {
            EnabledSslProtocols = SslProtocols.Tls13,
            ClientCertificates = [identity.Certificate],
            RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is X509Certificate2 presented && ServiceIdentity.FingerprintOf(presented) == expectedFingerprint
        },
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    }) { Timeout = TimeSpan.FromSeconds(15) };

    private readonly Dictionary<string, HttpClient> clients = new(StringComparer.Ordinal);

    // One pass over every peer with due items; returns how many items were delivered.
    public async Task<int> TickAsync(CancellationToken cancellation) {
        var delivered = 0;
        foreach (var (peer, items) in broker.DueForPeers()) {
            if (!clients.TryGetValue(peer.Fingerprint, out var client))
                clients[peer.Fingerprint] = client = ClientFor(identity, peer.Fingerprint);
            string[]? results = null;
            try {
                var payload = items.Select(i => new PeerItem(i.Kind, i.Offered, i.MessageId, i.SenderTool, i.Body, i.ExpiresUnixMs)).ToArray();
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

    public async Task RunAsync(CancellationToken cancellation) {
        while (!cancellation.IsCancellationRequested) {
            try {
                _ = await TickAsync(cancellation);
                await Task.Delay(2000, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        foreach (var client in clients.Values)
            client.Dispose();
    }
}

// The accepting side of pairing: connects to the inviter, requiring its certificate to match the
// invitation's fingerprint, and proves it holds the invitation's secret.
public static class PairingClient {
    public sealed record Result(string InviterName, string Check, string ExpectedCheck);

    public static async Task<Result> AcceptAsync(ServiceIdentity identity, Invitation invitation, string myName, string? myAddress, CancellationToken cancellation = default) {
        using var client = PeerDelivery.ClientFor(identity, invitation.Fingerprint);
        var proof = Invitation.Proof(invitation.Secret, invitation.Fingerprint, identity.Fingerprint);
        HttpResponseMessage response;
        try {
            response = await client.PostAsJsonAsync($"https://{invitation.Address}{PeerServer.PairPath}", new PairRequest(myName, myAddress, proof), cancellation);
        }
        // The inviter refuses the handshake once the invitation is used or expired; a different
        // certificate at that address is refused here. Either way, nothing was paired.
        catch (HttpRequestException e) {
            throw new InvalidOperationException($"Could not pair with {invitation.Address}: the invitation may have expired or been used, the address may be unreachable, or the service there is not the one that invited you.", e);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("The inviting service refused the invitation: it may have expired or been used. Ask for a new one.");
        var reply = await response.Content.ReadFromJsonAsync<PairResponse>(cancellation) ?? throw new InvalidOperationException("The inviting service sent no answer.");
        return new Result(reply.Name, reply.Check, Invitation.CheckCode(invitation.Secret, invitation.Fingerprint, identity.Fingerprint));
    }
}
