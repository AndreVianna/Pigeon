# Remote connections: design for 0.6.0-alpha.3

**Status: second draft for review, 2026-11-04.** It replaces the first design accepted earlier the same day (invitations to pair machines, then each topic shared by a person). That design is built on `alpha3-remote-connections` and worked between two real machines on a LAN; the review of that test led to this simpler model. Nothing in this draft is built yet. The release plan requires the design to be reviewed before code, because it opens Pigeon to the network.

## Scope

Set by the user on 2026-11-04 and unchanged:

- **Machines on the same network:** a LAN, or a private overlay such as Tailscale or WireGuard that the user already runs. Connections are direct. There is no relay, no NAT traversal and no exposure to the internet.
- **Other people's machines, not only the user's own:** a colleague's agent can talk to yours, so nothing is reachable from another machine unless a person on this machine approves it, and either side can end a connection at any time.

The goal is that two sessions on different machines share a two-member topic exactly as two local sessions do: messages queue in order, each side is woken by its own host, and peer messages remain untrusted data.

**Out of scope for alpha.3:** a relay or anything reachable from the internet; automatic discovery of machines (mDNS or broadcast); topics with more than two members; end-to-end encryption between agents beyond the encrypted link between services; authenticating the local loopback API.

## The model in one example

A topic has two seats. A seat can be taken by a local session or by a session on another machine, never both, so a topic open to other machines connects exactly one local session to one remote one.

1. On dev-laptop, the user asks their agent to open `project-1` to other machines. The topic is created locally and **listed**.
2. On office-laptop, the user asks their agent which topics dev-laptop has. dev-laptop answers `project-1`.
3. The office-laptop agent asks to join `project-1@dev-laptop`. Pigeon tells the office-laptop user: "Request submitted. Join code 123456."
4. dev-laptop's waiting session is woken with the request, and Pigeon asks the dev-laptop user, in a prompt the agent cannot answer: "office-laptop wants to join project-1 with Codex. Enter the join code to allow."
5. The office-laptop user tells the dev-laptop user the code by any means they like: in person, a call, a chat, an email.
6. The dev-laptop user enters it. If it matches, the join completes and the topic is full, so it is no longer listed.
7. Separately, a second office-laptop session opens `project-2`, and a third office-laptop session joins `project-2` with no `@machine`. That is a local topic, exactly as today; no code is asked for unless the topic was opened with approval required (see "Local topics").

Either user can end the connection at any time by telling their agent to disconnect.

## How it fits the current design

Each user runs one service bound to `127.0.0.1`, with no authentication; that does not change. Local agents, plugins and hooks keep using the loopback service.

The service has a second, separate listener: the **peer listener**. It is off until a person turns it on, accepts only TLS 1.3 connections with a client certificate, and serves only the small peer protocol below. It never serves the local API.

```
machine A                                          machine B
agent ── loopback ── service A ══ mutual TLS ══ service B ── loopback ── agent
                     (local API)   peer link    (local API)
```

A **remote topic** has one member on each machine. Each service stores its own side: its local member, the other machine's member as a peer seat, and the messages both ways. Sending writes the message locally and into an **outbox**; the service delivers it over the peer link and retries until the other side acknowledges it or it expires (24 hours). The receiving service queues it for its local member, whose wake works unchanged. A message keeps its ID on both machines, so a retried delivery is stored once.

## Identity

Each service has a long-term ECDSA P-256 key and a self-signed certificate, created on first use. Its **fingerprint** (SHA-256 of the public key) is its identity; there is no certificate authority. The private key stays in the current user's certificate store (DPAPI on Windows, the login Keychain on macOS, .NET's user-only store on Linux), never leaves the machine and is never printed. This part is already built.

Each service also has a **name**, which defaults to the computer's host name and can be changed with `pigeon peer name`. A name is a label shown to people, not an identity; fingerprints decide trust.

A machine becomes a **known machine** the first time a join between the two completes: each side pins the other's fingerprint, name and last address. There is no separate pairing step. `pigeon peer list` shows known machines and `pigeon peer remove <name>` forgets one, ending its topics at once.

## Opening a topic to other machines

- An agent opens a topic to other machines at the user's request: `pigeon_join` with `remote: true` creates or joins the topic and lists it. The CLI equivalent is `pigeon join <topic> as <session> using <tool> --remote`.
- Only **listed** topics accept requests from other machines. A topic is listed while it has a free seat and stops being listed once both seats are taken or the topic is closed. A topic opened without `remote` is never listed and never reachable from another machine.
- Letting an agent list a topic is safe because listing grants nothing: every join from another machine still needs the code from a person (below).
- The peer listener must be on for a topic to be listed. `pigeon peer listen` turns it on, once per machine, by a person. With no address it binds to this machine's private addresses (LAN and Tailscale ranges), never a public address and never `0.0.0.0`; `pigeon peer listen <address>` binds to one address; `pigeon peer listen off` closes it. Listing a topic while the listener is off fails with an error that says to run `pigeon peer listen`.
- The machine that requests a join also needs its listener on, because replies are delivered to it the same way. The error says so if it is off.

## Finding topics on another machine

- `pigeon_topics` with `machine: "dev-laptop"` (CLI: `pigeon topics on dev-laptop`) asks that machine for its listed topics. Only known machines get an answer, so topic names are never shown to a machine nobody has approved.
- For the **first contact** with a machine, there is nothing to list yet: the user gives the topic name and the address, for example `project-1@192.168.2.20` (or with a port, `project-1@192.168.2.20:49374`). After that join, the machine is known by its name.

## Joining with a code

A join request names a listed topic and the requesting session's tool name. Every request from another machine, known or not, needs a **join code**:

1. **The request.** The requesting service connects over TLS, sends the topic name, and both services exchange random values with a commitment: the requester first sends only a hash of its value, the listing side answers with its own value, and then the requester reveals its value. The code is 6 digits derived from both values and both fingerprints. Because each side fixes its value before seeing the other's, nobody (including a machine in the middle) can choose or predict the code.
2. **The requester sees the code.** `pigeon_join` returns "Request submitted. Join code 123456; give it to the person at dev-laptop. The join completes when they enter it." The session waits as it does for a local partner.
3. **The listing side asks its person.** The session holding the topic is woken with a join request. When its agent calls `pigeon_receive`, the Pigeon MCP server asks the user directly through **MCP elicitation**: "office-laptop (new machine, fingerprint ca99…) wants to join project-1 with Codex. Enter the join code to allow, or decline." Pigeon fills in every detail from what it verified (the fingerprint from the TLS handshake, the name for a known machine, "new machine" otherwise), never from text the requester chose; the tool name is shown as the requester's claim.
4. **The code goes straight to Pigeon.** The elicitation answer goes from the host's own UI to the MCP server, not through the model, so neither agent can answer it, and a message on any other topic cannot carry the code into it. The agent is told only "allowed", "declined" or "wrong code".
5. **Result.** A matching code completes the join: the requester's seat is taken, the topic stops being listed, and if the machine is new, both sides now know it. A decline, 3 wrong codes, or 10 minutes without an answer refuses the request; the seat stays free and the requester is told "not allowed".

**Hosts without elicitation, or nobody at the session.** The request also appears in `pigeon requests`, and a person allows it from any terminal with `pigeon allow <request> <code>` or refuses it with `pigeon deny <request>`. The CLI lists requests but never their codes.

**Limits on requests:** one open request per topic; a cap on open requests per service; requests from one address are rate-limited; requests for a topic that is not listed get the same "submitted" answer as listed ones, so nobody can probe for topic names, and are then dropped.

## Disconnecting

- Either user tells their agent to disconnect ("leave project-1", "disconnect from office-laptop"). The connect skill in every plugin says how: the agent calls `pigeon_leave`. It is safe to let agents do this, because it only takes access away; the worst a hostile message can do is make an agent leave, which the user can undo.
- Leaving a remote topic frees the seat on both sides, tells the other service at once (or on its next delivery if unreachable), and deletes the undelivered outbox. If the topic's opener left, it is no longer listed.
- People can also act from the terminal: `pigeon leave <topic> as <session>`, or `pigeon peer remove <name>` to end every topic with that machine and forget it.

## Local topics

Local topics work exactly as today, with one optional addition:

- **Approval required (opt-in).** A session can open a topic with `approval: true` (CLI `--approval`). Whoever joins it then gets a join code, and the session that opened the topic is asked for it by elicitation, as for a remote join. It guards against a wrong partner taking the seat first or an agent joining a topic it was talked into. Agents may turn it on, because it only tightens access. It does not protect against a malicious program running as the same user, which can read Pigeon's database directly; the operating system's user boundary is the protection there.
- **Who joined.** For every topic, local or remote, the session already waiting is told who joined (tool, and machine or session), so an unexpected partner is visible at once.

## What another machine can and cannot do

A known machine can:
- list this machine's listed topics;
- request to join a listed topic, which still needs a person's code;
- deliver messages into the topics it joined, into its own seat, within the limits below; acknowledge them; leave.

An unknown machine can only send join requests for a named topic, within the request limits. Neither can:
- join any topic without a person's code, or reach a topic that was never opened to other machines;
- see local session IDs (the other member is shown as `<tool> @<machine>`, never its session ID);
- read status, start waits or wakes, receive messages for local sessions, or stop the service.

**Limits per machine:** the per-topic hourly limit and 16 KiB message cap, a per-machine message rate, a cap on undelivered messages, a connection limit and a handshake rate limit. Exceeding them is refused and logged.

## Transport

- TLS 1.3 only, through Kestrel, with a client certificate required. Known machines are recognized by their pinned fingerprints; any other certificate reaches only the join-request path and the code exchange.
- Outgoing connections go to an address a user typed (first contact) or a known machine's last address. `pigeon peer address <name> <address>` updates it.
- Pigeon never changes the firewall. If the operating system asks, the person decides; the docs say which port to allow (49374 by default).

## Threats and mitigations

| Threat | Mitigation |
|---|---|
| Someone on the network reads or changes traffic | Mutual TLS 1.3; fingerprints pinned after the first join. |
| A machine in the middle of a first contact | The code depends on both real fingerprints and on random values fixed with a commitment, so a relaying machine produces different codes on the two sides and the join fails. |
| A machine joins without its person's or the other person's consent | Every remote join needs a code that only the requesting user is shown and only the listing user can enter, in a prompt the agent cannot answer. |
| An agent is talked into opening, listing or joining | Listing grants nothing; joining needs a person on the other side to enter the code. Agents can only narrow access by themselves (leave, approval on). |
| Guessing the code | 6 digits, 3 attempts, 10 minutes, one open request per topic. |
| Probing for topic names | Only known machines can list; requests for unlisted topics get the same answer as listed ones. |
| Request spam | Rate limits per address, one open request per topic, a cap per service; listener off by default. |
| A known machine turns hostile | It reaches only the topics it joined, within limits; it can request more joins, but each needs a code; its messages are untrusted data; `pigeon peer remove` cuts it off. |
| Prompt injection through a peer message | Peer messages are data. No message can approve a join, and the code never reaches the listing side's model. |
| The service's private key is stolen | OS-protected storage; the other side removes the machine; a new key means a new first contact. |
| Replay of old deliveries | TLS prevents replay; message IDs make a repeated delivery a no-op. |
| An agent with a shell runs `pigeon allow` itself | Possible today for any CLI action; the code still has to come from the requesting person. Listed as a known issue; closing it needs a prompt outside the CLI, later. |
| Another local user on the same machine | Unchanged: the loopback API is unauthenticated, so remote topics are recommended only on single-user machines. |

## Data

Messages from another machine are stored like local ones: unencrypted in SQLite, subject to retention, expiry and redaction. A remote topic's messages exist on both machines. Leaving or removing a machine deletes the undelivered outbox; each side keeps what it already received until it expires. Join requests are kept only until they are answered or expire; their codes are never written to disk in clear (only a hash for checking).

## Delivery semantics

- **Sent:** stored locally and in the outbox. **Delivered:** the other service acknowledged it. **Read:** stays local; no read receipts in alpha.3.
- `pigeon status` shows known machines (last contact, undelivered count), listed topics and open join requests.

## What changes from the first design

| First design (built) | This design |
|---|---|
| `pigeon peer invite`, `accept`, `confirm`: pair machines first | Removed. The first completed join makes a machine known. |
| `pigeon topic share`, `unshare`: a person opens each topic to one machine | Removed. A topic opened with `remote: true` is listed; a person approves each join with the code. |
| Join `<topic>@<peer>` only after sharing | Join `<topic>@<machine or address>` any listed topic, with the code. |
| Check code compared once, at pairing | Join code entered by the listing person for every remote join. |
| `pigeon peer listen <address>` required | Still once per machine, but the address is optional. |
| Service name required | Defaults to the host name. |
| `--no-tool` per share | Not carried over: the requester's tool name is shown to the person approving. Can return later if wanted. |

Kept as built: identity and key storage, the peer listener, the outbox and delivery, message IDs, limits, `peer list`, `peer remove`, `peer address`, `peer name`.

## Decisions taken in the review conversation (2026-11-04)

1. One handshake per topic, started by the agent at the user's request, approved by a code passed between the two people.
2. Topics are created locally and listed to other machines; a session joins `<topic>@<machine>` for a remote topic and `<topic>` for a local one.
3. The code is entered through elicitation, never in the chat; a terminal command is the fallback.
4. A code is needed for every remote join, including from a known machine.
5. Any user can disconnect by telling their agent; a skill covers it.
6. Local topics stay as they are, with approval by code as an opt-in per topic.

## Open questions

1. **Confirming a new machine on the requesting side.** Should the requester's person also confirm before Pigeon contacts a machine it has never seen ("Contact 192.168.2.20, a machine you have not joined before?")? It stops an agent from being talked into reaching out to an unknown machine, but adds a click to every first contact. **Recommendation:** yes, by elicitation, only for new machines.
2. **Elicitation support per host.** Claude Code supports MCP elicitation; Codex, Copilot, OpenCode and Antigravity must be checked. Hosts without it use the terminal fallback. **Recommendation:** check each before building, and list the result in the README.

## Implementation outline

1. Check MCP elicitation support in each host and record it.
2. Remove invitation pairing and topic sharing; keep identity, listener, outbox and peer store, with a migration for the stored shares.
3. Listed topics: `remote` on `pigeon_join` and `--remote` in the CLI; listing to known machines; `pigeon_topics` and `pigeon topics on <machine>`.
4. Join requests: the code exchange with commitment, request limits, first-contact by address, known machines on success.
5. Elicitation in the MCP server, plus `pigeon requests`, `allow` and `deny`.
6. Opt-in approval for local topics, and "who joined" notices.
7. Defaults: service name from the host name, `pigeon peer listen` with no address.
8. Skills in every plugin: open a topic to other machines, list, join, and disconnect.
9. Docs: README, [security design](security-design.md), [architecture](architecture.md), known issues, changelog.
10. Tests: two services in one test run (listing, codes right and wrong, expiry, limits, first contact, disconnect, local approval), then the two-machine host test again.
