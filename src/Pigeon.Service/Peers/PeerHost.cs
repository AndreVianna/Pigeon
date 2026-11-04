using System.Net;
using System.Text.Json;

using Pigeon.Core;

namespace Pigeon.Service.Peers;

// Runs the peer side of the service: the identity (loaded only once peers are used), the peer
// listener on the address the user chose, and the delivery loop. It also answers the local
// `peer_*` and `topic_share`/`topic_unshare` requests of the command line.
public sealed class PeerHost(Broker broker, IIdentityStore store) : IDisposable {
    public const int DefaultPort = 49374;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ServiceIdentity? identity;
    private CancellationTokenSource? listenerStop;
    private Task listener = Task.CompletedTask;
    private string? listening;
    private CancellationToken lifetime;
    // Invitations this service accepted, waiting for its person to confirm the check code.
    private readonly Dictionary<string, (string Address, long Expires)> accepted = new(StringComparer.Ordinal);

    public async Task RunAsync(CancellationToken cancellation) {
        lifetime = cancellation;
        if (broker.PeerSetting("listen") is not null || broker.HasPeers())
            await ApplyListenerAsync();
        while (!cancellation.IsCancellationRequested) {
            try {
                if (identity is { } current)
                    _ = await new PeerDelivery(broker, current).TickAsync(cancellation);
                await Task.Delay(2000, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        await StopListenerAsync();
    }

    private ServiceIdentity Identity() => identity ??= ServiceIdentity.LoadOrCreate(store);

    // "address" or "address:port"; IPv6 in brackets. The port defaults to 49374.
    public static IPEndPoint ParseEndpoint(string text) {
        text = text.Trim();
        if (IPAddress.TryParse(text, out var bare) && !text.Contains("]:", StringComparison.Ordinal))
            return new IPEndPoint(bare, DefaultPort);
        return IPEndPoint.TryParse(text, out var endpoint) && endpoint.Port is > 0 and <= 65535
            ? endpoint
            : throw new ArgumentException("Give an IP address, optionally with a port, for example 100.64.0.7 or 192.168.1.20:49374.");
    }

    private async Task ApplyListenerAsync() {
        var address = broker.PeerSetting("listen");
        if (address == listening && (address is null || !listener.IsCompleted))
            return;
        await StopListenerAsync();
        if (address is null)
            return;
        var server = new PeerServer(broker, Identity(), ParseEndpoint(address));
        listenerStop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        listener = server.RunAsync(listenerStop.Token);
        if (await Task.WhenAny(listener, server.Ready) == listener)
            await listener;
        listening = address;
    }

    private async Task StopListenerAsync() {
        if (listenerStop is null)
            return;
        await listenerStop.CancelAsync();
        try {
            await listener;
        }
        catch (OperationCanceledException) { }

        listenerStop.Dispose();
        listenerStop = null;
        listening = null;
    }

    private static string Text(JsonElement args, string key) => args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : throw new ArgumentException("Required argument missing");
    private static string? OptionalText(JsonElement args, string key) => args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<BrokerResult> ExecuteAsync(string operation, JsonElement args) {
        await gate.WaitAsync(lifetime);
        try {
            object? value = operation switch {
                "peer_status" => broker.PeerOverview(identity?.Fingerprint ?? (broker.HasPeers() ? Identity().Fingerprint : null)),
                "peer_name" => SetName(OptionalText(args, "name")),
                "peer_listen" => await ListenAsync(OptionalText(args, "address")),
                "peer_invite" => Invite(),
                "peer_accept" => await AcceptAsync(Text(args, "invitation"), OptionalText(args, "name")),
                "peer_accept_confirm" => AcceptConfirm(Text(args, "fingerprint"), Text(args, "name")),
                "peer_confirm" => broker.ConfirmPendingPeer(Text(args, "check"), OptionalText(args, "name")),
                "peer_remove" => new { removed = broker.RemovePeer(Text(args, "name")) },
                "peer_address" => SetAddress(Text(args, "name"), Text(args, "address")),
                "topic_share" => broker.Share(Text(args, "topic"), Text(args, "peer"), !(args.TryGetProperty("shareTool", out var t) && t.ValueKind == JsonValueKind.False)),
                "topic_unshare" => new { unshared = broker.Unshare(Text(args, "topic")) },
                _ => throw new ArgumentException("Unknown peer operation")
            };
            return new BrokerResult(true, value, null);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or IOException) {
            return new BrokerResult(false, null, e.Message);
        }
        finally {
            _ = gate.Release();
        }
    }

    private object SetName(string? name) {
        if (name is not null) {
            if (!Mailbox.IsValidPeerName(name))
                throw new ArgumentException("A name uses lowercase letters, digits and hyphens, up to 64 characters, for example ana-laptop.");
            broker.SetPeerSetting("name", name);
        }

        return new { name = broker.PeerSetting("name") };
    }

    private async Task<object> ListenAsync(string? address) {
        if (address is null || address == "off") {
            broker.SetPeerSetting("listen", null);
        }
        else {
            var endpoint = ParseEndpoint(address);
            broker.SetPeerSetting("listen", endpoint.ToString());
        }

        await ApplyListenerAsync();
        return new { listen = listening, fingerprint = listening is null ? null : Identity().Fingerprint };
    }

    private object Invite() {
        var name = broker.PeerSetting("name") ?? throw new InvalidOperationException("Name this service first: pigeon peer name <name>, for example ana-laptop.");
        var address = listening ?? throw new InvalidOperationException("Open the peer listener first: pigeon peer listen <address>, on the address the other machine will reach.");
        if (ParseEndpoint(address).Address.Equals(IPAddress.Any) || ParseEndpoint(address).Address.Equals(IPAddress.IPv6Any))
            throw new InvalidOperationException("The listener is on every address; listen on the one address the other machine will reach, so the invitation can name it.");
        var invitation = broker.CreateInvitation(Identity().Fingerprint, address, name);
        return new { invitation = invitation.Encode(), minutes = (int)Invitation.Lifetime.TotalMinutes, name };
    }

    private async Task<object> AcceptAsync(string code, string? name) {
        var myName = broker.PeerSetting("name") ?? throw new InvalidOperationException("Name this service first: pigeon peer name <name>, for example bruno-desktop.");
        var invitation = Invitation.Decode(code);
        var peerName = name ?? invitation.Name;
        if (!Mailbox.IsValidPeerName(peerName))
            throw new ArgumentException($"\"{peerName}\" is not a valid peer name; accept with 'as <name>'.");
        var result = await PairingClient.AcceptAsync(Identity(), invitation, myName, listening, lifetime);
        if (result.Check != result.ExpectedCheck)
            throw new InvalidOperationException("The inviting service answered with a check code that does not match the invitation; nothing was paired.");
        accepted[invitation.Fingerprint] = (invitation.Address, DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
        return new { inviter = result.InviterName, name = peerName, check = result.Check, fingerprint = invitation.Fingerprint, address = invitation.Address };
    }

    // The person compared the check code with the inviter and confirmed: store the inviter as a peer.
    private PeerInfo AcceptConfirm(string fingerprint, string name) {
        if (!accepted.Remove(fingerprint, out var pending) || pending.Expires < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new InvalidOperationException("No accepted invitation waits for confirmation; accept it again.");
        return broker.AddPeer(fingerprint, name, pending.Address);
    }

    private object SetAddress(string name, string address) {
        broker.SetPeerAddress(name, ParseEndpoint(address).ToString());
        return new { name, address };
    }

    public void Dispose() {
        identity?.Dispose();
        listenerStop?.Dispose();
        gate.Dispose();
    }
}
