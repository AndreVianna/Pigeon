using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

using Pigeon.Service;
using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// Pairing between two services on loopback that start out knowing nothing about each other.
public sealed class PairingTests : IAsyncLifetime {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-pair-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource stop = new();
    private readonly List<Task> servers = [];
    private long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private Broker ana = null!, bruno = null!;
    private ServiceIdentity anaId = null!, brunoId = null!, strangerId = null!;
    private string anaAddress = "", brunoAddress = "";

    private static int FreePort() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask InitializeAsync() {
        Directory.CreateDirectory(directory);
        ana = new Broker(Path.Combine(directory, "ana.db"), () => now);
        bruno = new Broker(Path.Combine(directory, "bruno.db"), () => now);
        ana.SetPeerSetting("name", "ana-laptop");
        bruno.SetPeerSetting("name", "bruno-desktop");
        anaId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "ana")));
        brunoId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "bruno")));
        strangerId = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "stranger")));
        var (anaPort, brunoPort) = (FreePort(), FreePort());
        (anaAddress, brunoAddress) = ($"127.0.0.1:{anaPort}", $"127.0.0.1:{brunoPort}");
        foreach (var (broker, identity, port) in new[] { (ana, anaId, anaPort), (bruno, brunoId, brunoPort) }) {
            var server = new PeerServer(broker, identity, new IPEndPoint(IPAddress.Loopback, port));
            servers.Add(server.RunAsync(stop.Token));
            await server.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Both_sides_see_the_same_check_code_and_are_paired_once_each_person_confirms() {
        var code = ana.CreateInvitation(anaId.Fingerprint, anaAddress, "ana-laptop").Encode();
        var result = await PairingClient.AcceptAsync(brunoId, Invitation.Decode(code), "bruno-desktop", brunoAddress);
        Assert.Equal(("ana-laptop", result.ExpectedCheck), (result.InviterName, result.Check));
        Assert.Matches("^[0-9]{6}$", result.Check);
        // Ana's side holds Bruno as pending until Ana confirms the same code; he is not a peer yet.
        var pending = Assert.Single(ana.PendingPeers());
        Assert.Equal((brunoId.Fingerprint, "bruno-desktop", result.Check), (pending.Fingerprint, pending.Name, pending.Check));
        Assert.False(ana.IsPeer(brunoId.Fingerprint));
        // A mistyped code names the waiting pairing and leaves it waiting.
        var wrong = Assert.Throws<InvalidOperationException>(() => ana.ConfirmPendingPeer("000000" == result.Check ? "111111" : "000000", null));
        Assert.Contains("does not match the pairing waiting here for \"bruno-desktop\"", wrong.Message);
        _ = Assert.Single(ana.PendingPeers());
        _ = ana.ConfirmPendingPeer(result.Check, null);
        // With nothing waiting, the message asks for a new invitation.
        Assert.Contains("No pairing is waiting here", Assert.Throws<InvalidOperationException>(() => ana.ConfirmPendingPeer(result.Check, null)).Message);
        _ = bruno.AddPeer(anaId.Fingerprint, "ana-laptop", anaAddress);
        Assert.True(ana.IsPeer(brunoId.Fingerprint));
        Assert.Empty(ana.PendingPeers());

        // Paired: a shared topic now carries messages.
        _ = ana.Execute("join", System.Text.Json.JsonSerializer.SerializeToElement(new { tool = "claude-code", sessionId = "ana-session", topic = "review" }));
        _ = ana.Share("review", "bruno-desktop", shareTool: true);
        Assert.Equal(1, await new PeerDelivery(ana, anaId).TickAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_invitation_works_once_only_with_its_secret_and_only_until_it_expires() {
        var invitation = ana.CreateInvitation(anaId.Fingerprint, anaAddress, "ana-laptop");
        _ = await PairingClient.AcceptAsync(brunoId, invitation, "bruno-desktop", brunoAddress);
        // Used: a second acceptance (here by another service) is refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() => PairingClient.AcceptAsync(strangerId, invitation, "stranger", null));

        // A forged invitation with the right fingerprint but another secret is refused.
        _ = ana.CreateInvitation(anaId.Fingerprint, anaAddress, "ana-laptop");
        var forged = new Invitation(anaAddress, anaId.Fingerprint, "ana-laptop", new byte[32]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => PairingClient.AcceptAsync(strangerId, forged, "stranger", null));

        // Expired after 10 minutes.
        var late = ana.CreateInvitation(anaId.Fingerprint, anaAddress, "ana-laptop");
        now += (long)Invitation.Lifetime.TotalMilliseconds + 1;
        await Assert.ThrowsAnyAsync<Exception>(() => PairingClient.AcceptAsync(strangerId, late, "stranger", null));
        Assert.DoesNotContain(ana.PendingPeers(), p => p.Fingerprint == strangerId.Fingerprint);
    }

    [Fact]
    public async Task An_open_invitation_lets_an_unpaired_service_reach_only_the_pairing_path() {
        _ = ana.CreateInvitation(anaId.Fingerprint, anaAddress, "ana-laptop");
        using var stranger = PeerDelivery.ClientFor(strangerId, anaId.Fingerprint);
        using var response = await stranger.PostAsJsonAsync($"https://{anaAddress}{PeerServer.DeliverPath}", new { Items = new[] { new { Kind = "offer", Offered = "x" } } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_acceptor_refuses_a_service_whose_certificate_does_not_match_the_invitation() {
        // Bruno's address with Ana's fingerprint: a machine-in-the-middle presenting another certificate.
        var invitation = ana.CreateInvitation(anaId.Fingerprint, anaAddress, "ana-laptop");
        var redirected = invitation with { Address = brunoAddress };
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => PairingClient.AcceptAsync(strangerId, redirected, "stranger", null));
        Assert.IsAssignableFrom<HttpRequestException>(refused.InnerException);
        Assert.Throws<ArgumentException>(() => Invitation.Decode("pigeon1.not-base64!"));
        Assert.Equal(invitation.Fingerprint, Invitation.Decode(invitation.Encode()).Fingerprint);
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
