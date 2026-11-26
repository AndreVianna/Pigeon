using System.Net;

using Pigeon.Service;
using Pigeon.Service.Peers;

namespace Pigeon.UnitTests;

// Machines that reach each other in memory instead of over HTTPS. A request goes straight to the
// listening machine's protocol handling, which sees the caller's fingerprint and address as the
// TLS handshake and the port header would present them. The handshake itself (pinning, TLS versions)
// is tested over real TLS in tests/Pigeon.IntegrationTests/Features/PeerTls.feature.
internal sealed class InMemoryPeerNetwork {
    private readonly Dictionary<string, PeerServer> listeners = new(StringComparer.Ordinal);

    // A machine listening at address, which is also where its own requests come from.
    public void Listen(string address, PeerServer server) => listeners[address] = server;

    public IPeerLink From(string address) => new Link(this, IPEndPoint.Parse(address));

    private sealed class Link(InMemoryPeerNetwork network, IPEndPoint from) : IPeerLink {
        private string Me => network.listeners[from.ToString()].Fingerprint;

        // As the handshake: nothing listening there, another identity than the pinned one, or a
        // listener that does not admit this machine, and the connection fails.
        private PeerServer Reach(string address, string? expectedFingerprint) {
            if (!network.listeners.TryGetValue(address, out var server))
                throw new HttpRequestException($"Nothing listens at {address}.");
            if (expectedFingerprint is not null && server.Fingerprint != expectedFingerprint || !server.Admits(Me))
                throw new HttpRequestException($"The handshake with {address} failed.");
            return server;
        }

        public Task<JoinStarted> StartJoinAsync(string address, string? expectedFingerprint, JoinStartRequest start, CancellationToken cancellation) {
            if (network.listeners.TryGetValue(address, out var other) && expectedFingerprint is not null && other.Fingerprint != expectedFingerprint)
                return Task.FromResult(new JoinStarted(other.Fingerprint, null, Refused: true));
            var server = Reach(address, expectedFingerprint);
            var answer = server.JoinStart(Me, from.Address, from.ToString(), start);
            return Task.FromResult(new JoinStarted(server.Fingerprint, answer, Refused: answer is null, Busy: answer is null));
        }

        public Task<bool> RevealJoinAsync(string address, string fingerprint, JoinRevealRequest reveal, CancellationToken cancellation)
            => Task.FromResult(Reach(address, fingerprint).JoinReveal(Me, reveal));

        public Task<JoinStatusResponse> JoinStatusAsync(string address, string fingerprint, JoinStatusRequest status, CancellationToken cancellation)
            => Task.FromResult(Reach(address, fingerprint).JoinStatus(Me, status));

        public Task<string[]> TopicsAsync(string address, string fingerprint, CancellationToken cancellation) {
            var server = Reach(address, fingerprint);
            return server.Knows(Me) ? Task.FromResult(server.Topics().Topics) : throw new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden);
        }

        public Task<string[]?> DeliverAsync(string address, string fingerprint, PeerItem[] items, CancellationToken cancellation) {
            var server = Reach(address, fingerprint);
            return Task.FromResult(server.Knows(Me) ? server.Deliver(Me, from.ToString(), new DeliverRequest(items))?.Results : null);
        }
    }
}
