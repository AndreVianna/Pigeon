# Remote connections: design for 0.6.0-alpha.3

**Status: reviewed and accepted by the user on 2026-11-04, with the recommended answers to the open questions (see the end). Built on `alpha3-remote-connections`; not yet tried across two real machines.** The release plan required this design to be reviewed before any code, because it opens Pigeon to the network for the first time.

## Scope

Set by the user on 2026-11-04:

- **Machines on the same network:** a LAN, or a private overlay such as Tailscale or WireGuard that the user already runs. Connections are direct. There is no relay, no NAT traversal and no exposure to the internet.
- **Other people's machines, not only the user's own:** a colleague's agent can talk to yours. So pairing a machine must not grant access to everything on it, and either side must be able to end the relationship.

The goal is that two sessions on different machines share a two-member topic exactly as two local sessions do today: messages queue in order, each side is woken by its own host as it is now, and peer messages remain untrusted data.

**Out of scope for alpha.3:** a relay or anything reachable from the internet; automatic discovery (mDNS or broadcast); topics with members on more than two machines; end-to-end encryption between agents beyond the encrypted link between services; hiding from a paired peer the traffic of the topics shared with it.

## How it fits the current design

Today each user runs one service bound to `127.0.0.1`, with no authentication: any local process can use any session ID. That stays exactly as it is. Local agents, plugins and hooks keep talking to the loopback service.

alpha.3 adds a second, separate listener to the same service: the **peer listener**. It is off by default, accepts only mutually authenticated TLS connections from paired peers, and serves only a small peer protocol (deliver a message, acknowledge, leave). It never serves the local API: a peer cannot join arbitrary topics, list sessions, read status or stop the service.

```
machine A (Ana)                                   machine B (Bruno)
agent ── loopback ── service A ══ mutual TLS ══ service B ── loopback ── agent
                     (local API)   peer link    (local API)
```

A **shared topic** has one member on each machine. Each service stores its own side: its local member, the remote member as a peer seat (`peer:<peer name>`), and the messages in both directions. Sending on a shared topic writes the message locally and puts it in an **outbox**; the service delivers it to the peer over the peer link and retries until the peer acknowledges it or it expires (24 hours, as today). The receiving service queues it for its local member, and the local wake works unchanged. Each message keeps its ID across both machines, so a retried delivery is stored once.

## Identity

Each service has a long-term key pair, created on first use of any peer command: an ECDSA P-256 key and a self-signed X.509 certificate. Its **fingerprint** (SHA-256 of the public key) is the service's identity. There is no certificate authority: each side pins the other's fingerprint at pairing.

Each service also has a human name chosen by its user (for example `ana-laptop`). A name is a label, not an identity; fingerprints decide trust, and each side may name a peer whatever it likes locally.

The private key is stored in the current user's certificate store, with no outside package: a non-exportable CNG key protected by DPAPI on Windows, the login Keychain on macOS, and .NET's user-only store under the home folder on Linux. Pigeon remembers its certificate's thumbprint next to its database and never touches other certificates in the store. Anyone who can read it can act as this service to its peers, so the key never leaves the machine and is never printed.

## Pairing

Pairing is always started by a person, never by an agent, and both people take part:

1. **Ana invites:** `pigeon peer invite` prints a one-time invitation: Ana's address and peer port, her service's fingerprint, and a random secret. It is valid for 10 minutes and for one acceptance.
2. **Ana sends it to Bruno** over a channel they both trust (a chat, an email, in person). Whoever sees the invitation could use it before Bruno does, which is why it is short-lived, single-use, and confirmed in step 4.
3. **Bruno accepts:** `pigeon peer accept <invitation>` connects to Ana's address, refuses to continue unless Ana's certificate matches the fingerprint in the invitation, and proves he holds the secret (an HMAC over both fingerprints, so the proof cannot be replayed to another service).
4. **Both confirm:** both machines show the same 6-digit check code, derived from both fingerprints and the secret. Each person compares it with the other (read aloud, or in the chat) and confirms. Only then does either side store the other as a peer.

After pairing, each side has the other's fingerprint, its chosen name and its last known address. Nothing else is shared.

**Removing a peer** (`pigeon peer remove <name>`) deletes its fingerprint and closes its connections at once; its shared topics end on this side, and the peer learns that on its next delivery. Either person can remove the other at any time, without the other's consent.

## Sharing topics

Pairing grants nothing on its own. Each shared topic is offered by a person:

- `pigeon topic share <topic> with <peer>` on Ana's machine offers one topic to one peer. The topic keeps its local member (or waits for one), and its second seat becomes Bruno's.
- Bruno's agent joins it as `<topic>@ana-laptop` (or Bruno runs `pigeon join` with that name). Until Ana shares the topic, joining it fails, so an agent cannot reach anything that was not offered.
- `pigeon topic unshare <topic>` ends the sharing on Ana's side and tells Bruno's service, whose member is notified on its next receive.

**Agents cannot pair, share or unshare.** These are command-line actions only, never MCP tools, because a peer message can try to persuade an agent to widen access (prompt injection). An agent can only join a topic a person already shared and use it like any other.

## What a paired peer can and cannot do

A peer can:
- deliver messages into the topics shared with it, into the seat reserved for it, within the limits below;
- acknowledge messages it received;
- leave a shared topic.

A peer cannot:
- join, create or list any other topic, or learn which other topics or peers exist;
- see local session IDs: it sees the other member as `ana-laptop` with its tool name (unless Ana's side turns that off), never the session ID;
- read status, start waits or wakes, receive messages for local sessions, or stop the service.

**Limits per peer:** the existing per-topic hourly limit and 16 KiB message cap, plus a per-peer message rate, a cap on stored undelivered messages, a connection limit, and a handshake rate limit. A peer that exceeds them is refused with an error and logged; it is not removed automatically.

## Transport

- TLS 1.3 only, through Kestrel's HTTPS support, with a client certificate required. The certificate validation accepts only fingerprints in the peer list and, during pairing, the one fingerprint the invitation names.
- The peer listener binds only to the address the user names (`pigeon peer listen <address>[:port]`, default port 49374), for example a Tailscale or LAN address, never `0.0.0.0` implicitly. It is off until then, and `pigeon peer listen off` closes it.
- Pigeon never changes the firewall. If the operating system asks, the person decides; the docs say which port and address to allow.
- Outgoing connections go only to the last known address of a paired peer. Addresses are not discovered, so a peer whose address changes is updated with `pigeon peer address <name> <address>`.

## Threats and mitigations

| Threat | Mitigation |
|---|---|
| Someone on the network reads or changes traffic | Mutual TLS 1.3; both sides pin fingerprints. |
| A machine-in-the-middle during pairing | The invitation carries Ana's fingerprint; the 6-digit check code catches a substituted acceptor. |
| A stolen invitation | Single use, 10-minute lifetime, and the check code must match on both screens. Ana sees who accepted and can remove them. |
| A scanner or unpaired machine on the network | The listener is off by default and binds to one chosen address; unpaired certificates fail the TLS handshake before any request; handshakes are rate-limited. |
| A paired peer turns hostile, or its machine is compromised | It reaches only the topics shared with it, within rate and size limits; its messages remain untrusted data that confer no permissions; either person removes it at once. |
| Prompt injection through a peer message | Unchanged rule: peer messages are data. No agent tool can pair, share or widen access. |
| A peer learns too much | It sees only its shared topics, the other member as a machine name, and messages in those topics; never session IDs, other topics or peers. |
| The service's private key is stolen | OS-protected storage; removing the stolen identity on the other side revokes it. A new key means pairing again. |
| Replay of old deliveries | TLS prevents replay on the wire; message IDs make a repeated delivery a no-op. |
| Another local user on the same machine | Unchanged and now more important: the loopback API has no authentication, so another user on Ana's machine could send on Ana's shared topics as her. The docs will recommend shared topics only on single-user machines. Authenticating the local API is a later step. |

## Data

Messages from a peer are stored like local ones: unencrypted in SQLite, subject to retention, expiry and redaction as today. A shared topic's messages exist on both machines. Removing a peer or unsharing a topic deletes its undelivered outbox, but each side keeps what it already received until it expires.

## Delivery semantics

- **Sent:** stored locally and in the outbox.
- **Delivered:** the peer service acknowledged it; it now waits there for Bruno's member, like a local message.
- **Read:** the local and remote receive acknowledgments stay local; alpha.3 does not send read receipts.

`pigeon status` shows each peer (connected or not, last contact) and each shared topic's outbox. `send` on a shared topic reports "delivered to peer" or "queued for peer".

## Decisions from the review (2026-11-04)

The user accepted the recommended answer to each open question:

1. **No live approval of each acceptance.** The check code that both people compare already catches a wrong acceptor; requiring the inviter's command to be running would force both people online at the same moment.
2. **The remote member's tool name is shared by default** (for example "Claude Code"), and the sharing side can turn it off per shared topic. Session IDs are never shared.
3. **A remote agent joins a shared topic as `<topic>@<peer>`.**
4. **No read receipts in alpha.3.**
5. **Authenticating the local loopback API is left for later.** It changes every plugin; alpha.3 documents the multi-user limitation instead.

## Implementation outline

1. Key pair, fingerprint and protected storage per platform.
2. Peer store (peers, shared topics, outbox) in the existing SQLite database, upgraded on first open.
3. The peer listener: Kestrel HTTPS with required client certificates, fingerprint pinning, the peer protocol, and limits.
4. Pairing: `peer invite`, `peer accept` with the check code, `peer list`, `peer remove`, `peer address`, `peer listen`.
5. Sharing: `topic share`, `topic unshare`, joining `<topic>@<peer>`, and the outbox with retries.
6. Status and docs: peers and outboxes in `pigeon status`; [security design](security-design.md) and [architecture](architecture.md) updated.
7. Tests: two services in one test run on loopback (pairing, sharing, delivery, retries, removal, refused access), and a host test with two machines on a LAN or Tailscale.
