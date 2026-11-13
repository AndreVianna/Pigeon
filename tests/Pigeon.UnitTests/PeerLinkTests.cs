using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

using Pigeon.Service;
using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// The listener's boundaries over real TLS: who gets through the handshake, and to which paths.
public sealed class PeerLinkTests : IAsyncLifetime {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-link-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource stop = new();
    private Task server = Task.CompletedTask;
    private Broker ana = null!;
    private ServiceIdentity anaId = null!, strangerId = null!;
    private int anaPort;

    public async ValueTask InitializeAsync() {
        Directory.CreateDirectory(directory);
        ana = new Broker(Path.Combine(directory, "ana.db"));
        anaId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "ana")));
        strangerId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "stranger")));
        using (var listener = new TcpListener(IPAddress.Loopback, 0)) {
            listener.Start();
            anaPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        ana.SetPeerSetting("listen", $"127.0.0.1:{anaPort}");
        var peer = new PeerServer(ana, anaId, [new IPEndPoint(IPAddress.Loopback, anaPort)]);
        server = peer.RunAsync(stop.Token);
        await peer.Ready.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private string Url(string path) => $"https://127.0.0.1:{anaPort}{path}";

    [Fact]
    public async Task An_unknown_machine_is_refused_at_the_handshake_while_no_topic_is_open() {
        using var stranger = PeerDelivery.ClientFor(strangerId, anaId.Fingerprint);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => stranger.PostAsJsonAsync(Url(PeerServer.JoinStartPath), new { }, TestContext.Current.CancellationToken));
    }

    // Found in CI on 2026-11-07: .NET on macOS accepts no TLS 1.3 connections, so a Mac talks TLS 1.2.
    [Fact]
    public async Task A_machine_limited_to_TLS_1_2_still_completes_the_mutual_handshake() {
        _ = ana.Execute("join", JsonSerializer.SerializeToElement(new { tool = "claude-code", sessionId = "ana-session", topic = "open", remote = true }));
        using var mac = new HttpClient(new SocketsHttpHandler {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                ClientCertificates = [strangerId.Certificate],
                RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is System.Security.Cryptography.X509Certificates.X509Certificate2 server && ServiceIdentity.FingerprintOf(server) == anaId.Fingerprint
            },
            UseProxy = false
        });
        using var listed = await mac.PostAsJsonAsync(Url(PeerServer.TopicsPath), new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
    }

    [Fact]
    public async Task A_client_refuses_a_server_with_another_identity() {
        _ = ana.Execute("join", JsonSerializer.SerializeToElement(new { tool = "claude-code", sessionId = "ana-session", topic = "open", remote = true }));
        using var misdirected = PeerDelivery.ClientFor(strangerId, strangerId.Fingerprint);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => misdirected.PostAsJsonAsync(Url(PeerServer.JoinStartPath), new { }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task While_a_topic_is_open_an_unknown_machine_reaches_only_the_join_paths() {
        _ = ana.Execute("join", JsonSerializer.SerializeToElement(new { tool = "claude-code", sessionId = "ana-session", topic = "open", remote = true }));
        using var stranger = PeerDelivery.ClientFor(strangerId, anaId.Fingerprint, port: 49999);
        var item = new PeerItem("message", "open", Guid.NewGuid().ToString("N"), "codex", "let me in", 60000);
        using (var delivered = await stranger.PostAsJsonAsync(Url(PeerServer.DeliverPath), new { Items = new[] { item } }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.Forbidden, delivered.StatusCode);
        using (var listed = await stranger.PostAsJsonAsync(Url(PeerServer.TopicsPath), new { }, TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.Forbidden, listed.StatusCode);
        using var started = await stranger.PostAsJsonAsync(Url(PeerServer.JoinStartPath), new { Topic = "open", Name = "stranger", Tool = "codex", Port = 49999, Commitment = JoinCode.Commit(JoinCode.NewNonce()) }, TestContext.Current.CancellationToken);
        Assert.True(started.IsSuccessStatusCode);
        Assert.Equal(0, ana.Execute("topics", JsonSerializer.SerializeToElement(new { sessionId = "ana-session" })) is { Ok: true } ? 0 : 1);
    }

    [Fact]
    public void The_join_code_depends_on_both_values_both_machines_and_the_topic() {
        var (a, b) = (JoinCode.NewNonce(), JoinCode.NewNonce());
        var code = JoinCode.Derive(a, b, "listing", "requester", "topic");
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(code, JoinCode.Derive(a, b, "listing", "requester", "topic"));
        Assert.NotEqual(code, JoinCode.Derive(a, b, "other", "requester", "topic"));
        Assert.NotEqual(code, JoinCode.Derive(a, b, "listing", "requester", "other"));
        Assert.True(JoinCode.Matches(JoinCode.Commit(a), a));
        Assert.False(JoinCode.Matches(JoinCode.Commit(a), b));
    }

    [Fact]
    public void Addresses_accept_host_names_and_default_the_port() {
        Assert.Equal("192.168.2.20:29374", PeerHost.ParseTarget("192.168.2.20"));
        Assert.Equal("dev-laptop.tail1234.ts.net:29374", PeerHost.ParseTarget("Dev-Laptop.tail1234.ts.net"));
        Assert.Equal("dev-laptop:50000", PeerHost.ParseTarget("dev-laptop:50000"));
        Assert.Throws<ArgumentException>(() => PeerHost.ParseTarget("bad host!"));
        Assert.Throws<ArgumentException>(() => PeerHost.ParseListen("dev-laptop"));
    }

    public async ValueTask DisposeAsync() {
        await stop.CancelAsync();
        await server;
        ana.Dispose();
        anaId.Dispose();
        strangerId.Dispose();
        stop.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
