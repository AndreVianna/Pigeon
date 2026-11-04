using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

using Pigeon.Service;
using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// Two services on loopback, each with its own identity and peer listener, as two machines would be.
public sealed class PeerLinkTests : IAsyncLifetime {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-link-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource stop = new();
    private readonly List<Task> servers = [];
    private Broker ana = null!, bruno = null!;
    private ServiceIdentity anaId = null!, brunoId = null!, strangerId = null!;
    private int anaPort, brunoPort;

    private static int FreePort() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask InitializeAsync() {
        Directory.CreateDirectory(directory);
        ana = new Broker(Path.Combine(directory, "ana.db"));
        bruno = new Broker(Path.Combine(directory, "bruno.db"));
        anaId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "ana")));
        brunoId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "bruno")));
        strangerId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "stranger")));
        (anaPort, brunoPort) = (FreePort(), FreePort());
        _ = ana.AddPeer(brunoId.Fingerprint, "bruno-desktop", $"127.0.0.1:{brunoPort}");
        _ = bruno.AddPeer(anaId.Fingerprint, "ana-laptop", $"127.0.0.1:{anaPort}");
        foreach (var (broker, identity, port) in new[] { (ana, anaId, anaPort), (bruno, brunoId, brunoPort) }) {
            var server = new PeerServer(broker, identity, new IPEndPoint(IPAddress.Loopback, port));
            servers.Add(server.RunAsync(stop.Token));
            await server.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static JsonElement Call(Broker broker, string operation, object args) {
        var result = broker.Execute(operation, JsonSerializer.SerializeToElement(args));
        Assert.True(result.Ok, result.Error);
        return JsonSerializer.SerializeToElement(result.Value);
    }

    [Fact]
    public async Task A_message_crosses_the_peer_link_and_wakes_the_session_on_the_other_machine() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "review" });
        _ = ana.Share("review", "bruno-desktop", shareTool: true);
        var anaDelivery = new PeerDelivery(ana, anaId);
        Assert.Equal(1, await anaDelivery.TickAsync(CancellationToken.None));
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-session", topic = "review@ana-laptop" });

        var sent = Call(ana, "send", new { sessionId = "ana-session", topic = "review", message = "over the wire" });
        Assert.Equal("queued-for-peer", sent.GetProperty("delivery").GetString());
        Assert.Equal(1, await anaDelivery.TickAsync(CancellationToken.None));
        // Bruno's session now has a wake waiting, exactly as for a local message.
        var claim = Call(bruno, "wake_claim", new { sessionId = "bruno-session", mode = "codex-queue" });
        Assert.Equal(JsonValueKind.Object, claim.ValueKind);
        var taken = Call(bruno, "take", new { sessionId = "bruno-session", topic = "review@ana-laptop" });
        Assert.Equal("over the wire", taken[0].GetProperty("Body").GetString());
        Assert.Equal("@ana-laptop", taken[0].GetProperty("SenderSessionId").GetString());

        _ = Call(bruno, "send", new { sessionId = "bruno-session", topic = "review@ana-laptop", message = "and back" });
        Assert.Equal(1, await new PeerDelivery(bruno, brunoId).TickAsync(CancellationToken.None));
        Assert.Equal("and back", Call(ana, "take", new { sessionId = "ana-session", topic = "review" })[0].GetProperty("Body").GetString());
    }

    [Fact]
    public async Task An_unreachable_peer_keeps_the_message_queued_for_a_later_attempt() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "review" });
        _ = ana.Share("review", "bruno-desktop", shareTool: true);
        _ = Call(ana, "send", new { sessionId = "ana-session", topic = "review", message = "while Bruno is away" });
        await stop.CancelAsync();
        await Task.WhenAll(servers);
        Assert.Equal(0, await new PeerDelivery(ana, anaId).TickAsync(CancellationToken.None));
        // Still waiting on Ana's side, now backed off rather than due at once.
        Assert.Equal(1, Call(ana, "topics", new { })[0].GetProperty("Waiting").GetInt32());
        Assert.Empty(ana.DueForPeers());
    }

    [Fact]
    public async Task An_unpaired_service_is_refused_at_the_handshake_and_a_wrong_server_is_refused_by_the_client() {
        // A certificate that is not a paired peer never gets a request through.
        using var stranger = PeerDelivery.ClientFor(strangerId, anaId.Fingerprint);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => stranger.PostAsJsonAsync($"https://127.0.0.1:{anaPort}{PeerServer.DeliverPath}", new { Items = Array.Empty<object>() }));
        // Bruno's client expecting Ana's fingerprint refuses a server presenting another one.
        using var misdirected = PeerDelivery.ClientFor(brunoId, strangerId.Fingerprint);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => misdirected.PostAsJsonAsync($"https://127.0.0.1:{anaPort}{PeerServer.DeliverPath}", new { Items = Array.Empty<object>() }));
    }

    [Fact]
    public async Task A_peer_cannot_deliver_into_a_topic_that_was_not_shared_with_it() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "private" });
        using var client = PeerDelivery.ClientFor(brunoId, anaId.Fingerprint);
        var item = new PeerItem("message", "private", Guid.NewGuid().ToString("N"), "codex", "let me in", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
        using var response = await client.PostAsJsonAsync($"https://127.0.0.1:{anaPort}{PeerServer.DeliverPath}", new { Items = new[] { item } });
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("[\"unknown\"]", JsonSerializer.Serialize((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results")));
        Assert.Equal(0, Call(ana, "topics", new { sessionId = "ana-session" })[0].GetProperty("Waiting").GetInt32());
    }

    public async ValueTask DisposeAsync() {
        await stop.CancelAsync();
        await Task.WhenAll(servers);
        ana.Dispose();
        bruno.Dispose();
        anaId.Dispose();
        brunoId.Dispose();
        strangerId.Dispose();
        stop.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
