using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

using Pigeon.Core;

namespace Pigeon.Service.Peers;

// Runs the peer side of the service: the identity (loaded only once other machines are used), the
// peer listener, the delivery loop and the requesting side of joins. It also answers the local
// `peer_*` requests of the command line and the agents' listing of another machine's topics.
public sealed partial class PeerHost(Broker broker, IIdentityStore store) : IDisposable {
    public const int DefaultPort = 49374;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ServiceIdentity? identity;
    private CancellationTokenSource? listenerStop;
    private Task listener = Task.CompletedTask;
    private string? listening;
    private IPEndPoint[] endpoints = [];
    private CancellationToken lifetime;
    private PeerDelivery? delivery;
    private long lastStatusPoll;

    public async Task RunAsync(CancellationToken cancellation) {
        lifetime = cancellation;
        if (broker.PeerSetting("listen") is not null)
            await ApplyListenerSafelyAsync();
        while (!cancellation.IsCancellationRequested) {
            try {
                await JoinsAsync(cancellation);
                if (identity is { } current)
                    _ = await (delivery ??= new PeerDelivery(broker, current, ListenPort)).TickAsync(cancellation);
                await Task.Delay(1000, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        delivery?.Dispose();
        await StopListenerAsync();
    }

    private ServiceIdentity Identity() => identity ??= ServiceIdentity.LoadOrCreate(store);
    private int ListenPort() => endpoints.Length == 0 ? 0 : endpoints[0].Port;

    // The computer's host name, tidied, until a person names the service.
    public static string DefaultName() => Mailbox.TidyName(Environment.MachineName);
    private string Name() => broker.PeerSetting("name") ?? DefaultName();

    // ---- The requesting side of joins ----

    private async Task JoinsAsync(CancellationToken cancellation) {
        foreach (var request in broker.OutgoingDue("contact")) {
            try {
                var contact = await JoinClient.StartAsync(Identity(), request.Target, request.Known ? request.Fingerprint : null, request.Offered, Name(), request.Tool, ListenPort(), request.Nonce, cancellation);
                if (contact.Fingerprint == Identity().Fingerprint)
                    throw new InvalidOperationException("that address is this machine.");
                broker.OutgoingContacted(request, contact.Fingerprint, contact.Name, contact.Request, contact.Code, contact.NeedsCode);
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException or IOException or FormatException && !cancellation.IsCancellationRequested) {
                broker.OutgoingFailed(request, e is InvalidOperationException ? e.Message : $"{request.Target} could not be reached.");
            }
        }

        // Ask about open requests every two seconds.
        if (broker.Now() - lastStatusPoll < 2000)
            return;
        lastStatusPoll = broker.Now();
        foreach (var request in broker.OutgoingDue("waiting")) {
            try {
                var (state, tool) = await JoinClient.StatusAsync(Identity(), request.Target, request.Fingerprint!, request.RemoteId!, ListenPort(), cancellation);
                if (state is "allowed" or "denied")
                    broker.OutgoingAnswered(request, state, tool);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or IOException && !cancellation.IsCancellationRequested) { }
        }
    }

    // ---- Addresses ----

    // "address" or "address:port" for the listener; IPv6 in brackets. The port defaults to 49374.
    public static IPEndPoint ParseListen(string text) {
        text = text.Trim();
        if (IPAddress.TryParse(text, out var bare) && !text.Contains("]:", StringComparison.Ordinal))
            return new IPEndPoint(bare, DefaultPort);
        return IPEndPoint.TryParse(text, out var endpoint) && endpoint.Port is > 0 and <= 65535
            ? endpoint
            : throw new ArgumentException("Give an IP address, optionally with a port, for example 192.168.1.20 or 100.64.0.7:49374.");
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostPattern();

    // Where another machine is reached: an IP address or a host name, optionally with a port.
    public static string ParseTarget(string text) {
        text = text.Trim().ToLowerInvariant();
        if (IPAddress.TryParse(text, out var bare) && !text.Contains("]:", StringComparison.Ordinal))
            return new IPEndPoint(bare, DefaultPort).ToString();
        if (IPEndPoint.TryParse(text, out var endpoint) && text.Contains(':') && endpoint.Port is > 0 and <= 65535)
            return endpoint.ToString();
        var colon = text.LastIndexOf(':');
        var host = colon < 0 ? text : text[..colon];
        var port = DefaultPort;
        if (colon >= 0 && (!int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            throw new ArgumentException($"\"{text}\" is not a valid address. Give a machine name, an IP address or a host name, optionally with a port.");
        return HostPattern().IsMatch(host)
            ? $"{host}:{port.ToString(CultureInfo.InvariantCulture)}"
            : throw new ArgumentException($"\"{text}\" is not a valid address. Give a machine name, an IP address or a host name, optionally with a port.");
    }

    private static readonly string[] VirtualAdapters = ["wsl", "hyper-v", "vethernet", "docker", "virtualbox", "vmware", "vbox", "loopback"];

    // This machine's private addresses: LAN ranges and Tailscale's, never a public address,
    // and not the virtual adapters of WSL, Hyper-V, Docker or virtual machines.
    public static IPAddress[] PrivateAddresses() => [.. NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && !VirtualAdapters.Any(v => n.Name.Contains(v, StringComparison.OrdinalIgnoreCase) || n.Description.Contains(v, StringComparison.OrdinalIgnoreCase)))
        .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a))
        .Distinct()];

    private static bool IsPrivate(IPAddress address) {
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127);
    }

    private IPEndPoint[] Resolve(string setting) {
        if (!setting.StartsWith("auto", StringComparison.Ordinal))
            return [ParseListen(setting)];
        var port = setting.Length > 5 && int.TryParse(setting[5..], CultureInfo.InvariantCulture, out var p) ? p : DefaultPort;
        var addresses = PrivateAddresses();
        return addresses.Length == 0
            ? throw new InvalidOperationException("This machine has no LAN or Tailscale address to listen on; name one: pigeon peer listen <address>.")
            : [.. addresses.Select(a => new IPEndPoint(a, port))];
    }

    // ---- The listener ----

    private async Task ApplyListenerSafelyAsync() {
        try {
            await ApplyListenerAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or SocketException) { }
    }

    private async Task ApplyListenerAsync() {
        var setting = broker.PeerSetting("listen");
        var wanted = setting is null ? [] : Resolve(setting);
        if (setting == listening && wanted.SequenceEqual(endpoints) && (setting is null || !listener.IsCompleted))
            return;
        await StopListenerAsync();
        if (setting is null)
            return;
        var server = new PeerServer(broker, Identity(), wanted);
        listenerStop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        listener = server.RunAsync(listenerStop.Token);
        if (await Task.WhenAny(listener, server.Ready) == listener)
            await listener;
        listening = setting;
        endpoints = wanted;
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
        endpoints = [];
    }

    // ---- Local requests ----

    private static string Text(JsonElement args, string key) => args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : throw new ArgumentException("Required argument missing");
    private static string? OptionalText(JsonElement args, string key) => args.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<BrokerResult> ExecuteAsync(string operation, JsonElement args) {
        await gate.WaitAsync(lifetime);
        try {
            object? value = operation switch {
                "peer_status" => broker.PeerOverview(identity?.Fingerprint ?? (broker.HasPeers() ? Identity().Fingerprint : null), [.. endpoints.Select(e => e.ToString())]),
                "peer_name" => SetName(OptionalText(args, "name")),
                "peer_listen" => await ListenAsync(OptionalText(args, "address")),
                "peer_remove" => new { removed = broker.RemovePeer(Text(args, "name")) },
                "peer_address" => SetAddress(Text(args, "name"), Text(args, "address")),
                "peer_topics" => await TopicsOnAsync(Text(args, "machine")),
                _ => throw new ArgumentException("Unknown peer operation")
            };
            return new BrokerResult(true, value, null);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or IOException or SocketException) {
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

        return new { name = Name() };
    }

    // With no address, the listener binds to this machine's LAN and Tailscale addresses.
    private async Task<object> ListenAsync(string? address) {
        if (address == "off")
            broker.SetPeerSetting("listen", null);
        else if (address is null)
            broker.SetPeerSetting("listen", "auto");
        else if (address.StartsWith("auto:", StringComparison.Ordinal) && int.TryParse(address[5..], CultureInfo.InvariantCulture, out var port) && port is > 0 and <= 65535)
            broker.SetPeerSetting("listen", address);
        else
            broker.SetPeerSetting("listen", ParseListen(address).ToString());
        await ApplyListenerAsync();
        return new { listen = listening, addresses = endpoints.Select(e => e.ToString()).ToArray(), name = Name() };
    }

    private object SetAddress(string name, string address) {
        broker.SetPeerAddress(name, ParseTarget(address));
        return new { name, address = ParseTarget(address) };
    }

    private async Task<object> TopicsOnAsync(string machine) {
        var peer = broker.Peers().FirstOrDefault(p => p.Name == machine)
            ?? throw new InvalidOperationException($"{machine} is not a machine you joined before; only known machines list their topics. Join a topic there by address the first time: <topic>@<address>.");
        if (peer.Address is null)
            throw new InvalidOperationException($"No address is known for {machine}.");
        try {
            return new { machine, topics = await JoinClient.TopicsAsync(Identity(), peer.Address, peer.Fingerprint, ListenPort(), lifetime) };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) {
            throw new InvalidOperationException($"{machine} could not be reached at {peer.Address}.", e);
        }
    }

    public void Dispose() {
        identity?.Dispose();
        listenerStop?.Dispose();
        gate.Dispose();
    }
}
