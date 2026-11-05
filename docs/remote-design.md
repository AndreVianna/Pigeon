# Remote connections: design for 0.6.0-alpha.3

**Status: agreed by the user and built on `alpha3-remote-connections` on 2026-11-04, after elicitation was checked in every host; not yet host-tested across two machines.** It replaces the first design accepted earlier the same day (invitations to pair machines, then each topic shared by a person), which was built first and worked between two real machines on a LAN. The review of that test led to the second draft; an independent review of the second draft led to this one, which keeps the cheap fixes and accepts the rest as known limits (see "Accepted limits"). The aim is a middle ground: not a foolproof system, but not a fragile one.

## Scope

Set by the user on 2026-11-04 and unchanged:

- **Machines on the same network:** a LAN, or a private overlay such as Tailscale or WireGuard that the user already runs. Connections are direct. There is no relay, no NAT traversal and no exposure to the internet.
- **Other people's machines, not only the user's own:** a colleague's agent can talk to yours, so nothing is reachable from another machine unless a person on this machine approves it, and either side can end a connection at any time.

The goal is that two sessions on different machines share a two-member topic exactly as two local sessions do: messages queue in order, each side is woken by its own host, and peer messages remain untrusted data.

**Assumed:** both machines can open an inbound port on the network they share (true on home and most small-office networks; often not on managed corporate devices or isolated guest Wi-Fi), and the two people can pass a 6-digit code to each other once per pair of machines.

**Out of scope for alpha.3:** a relay or anything reachable from the internet; automatic discovery of machines; topics with more than two members; end-to-end encryption between agents beyond the encrypted link between services; authenticating the local loopback API.

## The model in one example

A topic has two seats. A seat can be taken by a local session or by a session on another machine, so a topic open to other machines connects exactly one local session to one remote one.

1. On dev-laptop, the user asks their agent to open `project-1` to other machines. The topic is created locally and **listed**.
2. On office-laptop, the user asks their agent to join `project-1@dev-laptop` (the first time, by address: `project-1@192.168.2.20`).
3. office-laptop has never joined dev-laptop, so Pigeon asks the office-laptop user directly: "Contact 192.168.2.20, a machine you have not joined before? Your join code will be 123456; give it to the person there." The agent sees only "request submitted".
4. dev-laptop's waiting session is woken, and Pigeon asks the dev-laptop user, in a prompt the agent cannot answer: "A new machine, office-laptop, wants to join project-1 with Codex. Enter the join code its person gave you. Never enter a code your agent relays."
5. The two people pass the code by any means they like: in person, a call, a chat.
6. The dev-laptop user enters it. The join completes, both machines now know each other, and the topic is full, so it is no longer listed.
7. Later, office-laptop joins another listed topic on dev-laptop. The machines are known, so there is no code: the dev-laptop user is asked "office-laptop wants to join project-2 with Codex. Allow?"
8. Meanwhile, two sessions on office-laptop can share a local topic `project-3`, joined with no `@machine`, exactly as today.

Either user can end the connection at any time by telling their agent to disconnect.

## How it fits the current design

Each user runs one service bound to `127.0.0.1`, with no authentication; that does not change. Local agents, plugins and hooks keep using the loopback service.

The service has a second, separate listener: the **peer listener**. It is off until turned on, accepts only TLS 1.3 connections with a client certificate, and serves only the peer protocol. It never serves the local API.

```
machine A                                          machine B
agent ── loopback ── service A ══ mutual TLS ══ service B ── loopback ── agent
                     (local API)   peer link    (local API)
```

A **remote topic** has one member on each machine. Each service stores its own side: its local member, the other machine's member as a peer seat, and the messages both ways. Sending writes the message locally and into an **outbox**; the service delivers it over the peer link and retries until the other side acknowledges it or it expires (24 hours). The receiving service queues it for its local member, whose wake works unchanged. A message keeps its ID on both machines, so a retried delivery is stored once, and it carries its remaining lifetime rather than an absolute expiry time, so clocks that differ between machines do not matter.

## Identity

Each service has a long-term ECDSA P-256 key and a self-signed certificate, created on first use. Its **fingerprint** (SHA-256 of the public key) is its identity; there is no certificate authority. The private key stays in the current user's certificate store: DPAPI on Windows and the login Keychain on macOS; on Linux, .NET's store is a file readable only by the user. It never leaves the machine and is never printed. This part is already built.

Each service has a **name**, which defaults to the computer's host name, lowercased and trimmed of suffixes such as `.local`, and can be changed with `pigeon peer name`. A name is a label shown to people, not an identity; fingerprints decide trust. If two known machines would share a name, the newer one gets a number (`dev-laptop-2`).

A machine becomes a **known machine** the first time a join between the two completes: each side pins the other's fingerprint, name and address. There is no separate pairing step. `pigeon peer list` shows known machines and `pigeon peer remove <name>` forgets one, ending its topics at once.

**Identity changed.** If a machine with a known name or address presents a different fingerprint (a reinstall or a lost key), Pigeon treats it as a new machine and says so: "dev-laptop's identity changed. If it was reinstalled, remove it with `pigeon peer remove dev-laptop` and join again." Nothing is pinned until then.

## Opening a topic to other machines

- An agent opens a topic to other machines at the user's request: `pigeon_join` with `remote: true` creates or joins the topic and lists it. The CLI equivalent is `--remote`.
- Only **listed** topics accept requests from other machines. A topic is listed while it has a free seat and is listed again if its remote member leaves. A topic opened without `remote` is never listed and never reachable from another machine.
- Letting an agent list a topic is acceptable because listing grants nothing: every join from another machine still needs a person's answer.
- The peer listener must be on for a topic to be listed, on both machines, because replies are delivered to the requester the same way. `pigeon peer listen` turns it on. With no address it binds to this machine's LAN and Tailscale addresses, excluding virtual adapters (WSL, Hyper-V, Docker) and never a public address or `0.0.0.0`; it prints the addresses another machine can use. `pigeon peer listen <address>` binds to one address; `pigeon peer listen off` closes it.
- Opening the listener is a terminal command, and an agent with a shell can run it. That is accepted: an open listener reaches nothing without a listed topic and a person's answer.

## Finding topics on another machine

- `pigeon_topics` with `machine: "dev-laptop"` (CLI: `pigeon topics on dev-laptop`) asks that machine for its listed topics. Only known machines get an answer.
- For the **first contact**, the user gives the topic name and the machine's address or host name: `project-1@192.168.2.20`, `project-1@dev-laptop.tail1234.ts.net`, or with a port, `project-1@192.168.2.20:49374`. After that join, the machine is known by its name.
- A known machine's address is updated each time it makes an authenticated contact, so an address that changes through DHCP heals itself once either side reaches the other. `pigeon peer address <name> <address>` sets it by hand.

## Joining

The code and every approval go to people, never to a model. Agents only see the outcome: "request submitted", "allowed", "declined", "not answered".

### First contact (a new machine): join code

1. **The requesting person confirms and sees the code.** Before contacting a machine it has never joined, Pigeon asks the requesting user by elicitation: "Contact 192.168.2.20, a machine you have not joined before?" On yes, the services connect over TLS and exchange random values with a commitment (the requester first sends only a hash of its value, the listing side answers with its own value, then the requester reveals its value). The 6-digit code is derived from both values and both fingerprints, so neither side, nor a machine in the middle relaying the exchange, can choose it. The elicitation then shows the code: "Join code 123456; give it to the person at that machine." The code is never in a tool result.
2. **The listing person enters it.** As soon as the request arrives, the Pigeon MCP server of the session holding the topic asks the user by elicitation, outside any tool call: the session is not woken and no model turn is spent. The prompt shows details Pigeon verified itself: "A new machine, office-laptop, wants to join project-1 with Codex. Enter the join code its person gave you. Never enter a code your agent relays." The name and tool are the requester's claims and are shown as such.
3. **Result.** A matching code completes the join and both sides pin each other. A decline, 3 wrong codes, or 10 minutes without an answer refuses it; the seat stays free.

### Known machines: Allow or Deny

The fingerprint is already pinned, so a code would add nothing. The listing user is asked "office-laptop wants to join project-2 with Codex. Allow?" and answers Allow or Deny. The requesting side needs no confirmation. Unattended agent pairs can reconnect after a reboot with a single click on one side.

### How prompts are handled

Measured in each host with a probe on 2026-11-04 ([results](../experiments/elicitation-probe/README.md)):

- Every prompt has Pigeon's own time limit (10 minutes); no reply, `decline` and `cancel` (Esc, in every host) all mean Deny.
- An answer counts only for a request that is still open. Hosts can deliver an answer after the moment has passed (Copilot) or replay an unanswered prompt in a later session (OpenCode); those are ignored.
- The requesting agent is told "request pending; do not retry", because a model whose call fails may retry on its own (Copilot), and one open request per topic stops duplicates.
- Pigeon raises prompts outside tool calls, which Claude Code, Codex, Copilot and OpenCode all support; Copilot cancels a tool call after 3 minutes, too short for two people to pass a code.

### Antigravity: local topics only

Antigravity advertises elicitation but cancels every prompt without showing it, so it could approve a join only through the terminal. Rather than make that the only path for one host, **Antigravity sessions are limited to local topics** (decided by the user on 2026-11-04):

- `pigeon_join` with `remote: true` or a `<topic>@<machine>` name, and `pigeon_topics` with a machine, are refused for an Antigravity session: "Remote topics are not available in Antigravity, which cannot ask you for approval. Use it for local topics."
- Opt-in approval on local topics is not offered to Antigravity sessions either, for the same reason; its local topics work as before.
- Pigeon recognizes the host by the tool name its plugin reports. That is a product limit, not a security boundary: any local program can claim another tool name.
- The README lists it as a host limitation. It is lifted if Antigravity starts showing prompts; the [elicitation probe](../experiments/elicitation-probe/README.md) re-checks that in a few minutes.

### Without elicitation

Requests with nobody at the session, and headless runs (Codex and Copilot decline at once; OpenCode never answers), use the terminal: `pigeon requests` lists open requests on both sides, `pigeon requests --show <request>` shows the requesting side its code (only in a terminal, never through the MCP server), and `pigeon allow <request> [<code>]` or `pigeon deny <request>` answers on the listing side.

### Limits

One open request per topic; requests from one address are rate-limited; Deny refuses at once. A request for a topic that is not listed gets the same "request submitted" answer as a listed one, so nobody can probe for topic names, and is then dropped; the requester is told "if nobody answers within 10 minutes, check the topic name and address."

## Disconnecting

- Either user tells their agent to disconnect ("leave project-1", "disconnect from office-laptop"). The connect skill in every plugin says how: the agent calls `pigeon_leave`. Agents may do this freely, because it only takes access away.
- Leaving a remote topic frees the seat on both sides, tells the other service at once (or on its next delivery), and deletes the undelivered outbox.
- From a terminal: `pigeon leave <topic> as <session>`, or `pigeon peer remove <name>` to end every topic with that machine and forget it.

## Local topics

Local topics work exactly as today, with one optional addition:

- **Approval required (opt-in).** A session can open a local topic with `approval: true` (CLI `--approval`). Whoever joins it is then shown a join code by elicitation, and the session that opened the topic is asked for it, as for a first contact. It guards against a wrong partner taking the seat first or an agent joining a topic it was talked into. Agents may turn it on, because it only tightens access. It does not protect against a malicious program running as the same user, which can read Pigeon's database directly.
- **Who joined.** For remote topics and topics that require approval, the session already waiting is told who joined (tool, and machine or session), so an unexpected partner is visible at once. Plain local topics keep today's behavior; a notice there would add a wake to every join.

## What another machine can and cannot do

A known machine can list this machine's listed topics, request to join one (which a person must allow), deliver messages into the topics it joined within the limits below, acknowledge them, and leave. An unknown machine can only send join requests for a named topic, within the request limits. Neither can join without a person's answer, reach a topic that was never opened to other machines, see local session IDs, read status, start waits or wakes, receive messages for local sessions, or stop the service.

**Limits per machine:** the per-topic hourly limit and 16 KiB message cap, a per-machine message rate, a cap on undelivered messages, a connection limit and a handshake rate limit.

## Transport

- TLS 1.3 only, through Kestrel, with a client certificate required. Known machines are recognized by pinned fingerprints. **Change from the first design:** while any topic is listed, the listener also accepts unknown certificates, which reach only the join-request path; the first design accepted them only while an invitation was open.
- Outgoing connections go to an address or host name a user typed (first contact) or a known machine's last address.
- The peer protocol moves to version 2. A machine running the first design gets "the other machine runs an older Pigeon; update it".
- Pigeon never changes the firewall. If the operating system asks, the person decides; the docs say which port to allow (49374 by default).

## Threats and mitigations

| Threat | Mitigation |
|---|---|
| Someone on the network reads or changes traffic | Mutual TLS 1.3; fingerprints pinned after the first join. |
| Two agents pass a code between them to approve a join | The code is shown only by elicitation or a terminal, never in a tool result, so the requesting model never has it. |
| An agent asks its user to type a code it relays | The prompt says never to enter a code an agent relays. Social engineering is not fully preventable; this is the reminder at the moment it matters. |
| An agent is talked into contacting a new machine | The requesting person confirms every first contact. |
| A machine in the middle fools the listing side | The code depends on both real fingerprints and on values fixed with a commitment, so the code the listing person types does not match. |
| Guessing the code | 6 digits, 3 attempts, 10 minutes, one open request per topic. |
| Probing for topic names | Only known machines can list; requests for unlisted topics get the same answer as listed ones. |
| A known machine turns hostile | It reaches only the topics it joined; more joins need a person's Allow; its messages are untrusted data; `pigeon peer remove` cuts it off. |
| Prompt injection through a peer message | Peer messages are data; no message can approve a join. |
| A reinstalled machine, or one posing as a known machine | A different fingerprint is never accepted under a known name; Pigeon says the identity changed. |
| Replay of old deliveries | TLS prevents replay; message IDs make a repeated delivery a no-op. |

## Accepted limits

Known and accepted for alpha.3, to be listed in [known issues](known-issues.md):

1. **The requesting side is not proven on first contact.** The code is checked only on the listing side, so a machine that impersonates the listing machine at the right address on the user's own network could accept the request itself and be pinned. It needs an active attacker on the local network; an accidental mix-up cannot pass, because the wrong machine's person would have to enter a code nobody gave them. A PAKE, which uses the code to prove both sides at once, is the later fix.
2. **Both machines need an inbound port.** Managed corporate devices and isolated guest Wi-Fi often prevent it. Carrying replies back over the requester's outgoing connection is the later fix.
3. **Agents with a shell can use the CLI**, including `pigeon allow`, `pigeon peer listen` and `pigeon requests --show`. The code still has to reach the listing side through a person or another agent channel the user set up. Closing this needs an authenticated local API, later.
4. **Hosts may answer elicitations without a person:** headless runs decline them, and a Claude Code hook can answer them. Documented per host.
5. **Request spam is limited only by the basics:** one request per topic, a rate limit and Deny. No block lists or grouped prompts.
6. **Another local user on the same machine** can use its unauthenticated loopback API, including remote topics. Remote topics are recommended only on single-user machines, and the README says so prominently.

## Data

Messages from another machine are stored like local ones: unencrypted in SQLite, subject to retention, expiry and redaction. A remote topic's messages exist on both machines. Leaving or removing a machine deletes the undelivered outbox; each side keeps what it already received until it expires. Join requests are kept only until answered or expired, with only a hash of the code. The first design's pending pairings are dropped, not migrated; its per-topic link table is kept as the record of each remote topic. alpha.3 has had no users beyond its own test.

## Delivery semantics

- **Sent:** stored locally and in the outbox. **Delivered:** the other service acknowledged it. **Read:** stays local; no read receipts in alpha.3.
- `pigeon status` shows known machines (last contact, undelivered count), listed topics and open join requests.

## What changes from the first design

| First design (built) | This design |
|---|---|
| `pigeon peer invite`, `accept`, `confirm`: pair machines first | Removed. The first completed join, with a code, makes a machine known. |
| `pigeon topic share`, `unshare`: a person opens each topic to one machine | Removed. A topic opened with `remote: true` is listed; a person allows each join. |
| Join `<topic>@<peer>` only after sharing | Join `<topic>@<machine, address or host name>` for any listed topic. |
| Check code compared once, at pairing | Join code on first contact; Allow or Deny for known machines. |
| `pigeon peer listen <address>` with an IP only | Address optional; host names accepted; addresses update on contact. |
| Service name required | Defaults to the tidied host name. |
| Unknown certificates accepted only while an invitation is open | Accepted, for join requests only, while a topic is listed. |
| Absolute expiry sent between machines | Remaining lifetime sent instead. |
| `--no-tool` per share | Not carried over; can return later. |

Kept as built: identity and key storage, the listener and TLS setup, the outbox and delivery, message IDs, limits, `peer list`, `peer remove`, `peer address`, `peer name`.

## Decisions taken in the review conversation (2026-11-04)

1. One handshake per topic, started by the agent at the user's request.
2. Topics are created locally and listed to other machines; a session joins `<topic>@<machine>` for a remote topic and `<topic>` for a local one.
3. Answers go through elicitation, never the chat; the terminal is the fallback.
4. A join code on first contact with a machine; Allow or Deny for known machines. (Revised after the independent review: once a fingerprint is pinned, a code adds no protection.)
5. Any user can disconnect by telling their agent; a skill covers it.
6. Local topics stay as they are, with approval by code as an opt-in per topic.
7. The requesting person confirms before Pigeon contacts a new machine, and sees the code in that same prompt.
8. Elicitation support is confirmed in every host before building: Claude Code, Codex, Copilot, OpenCode and Antigravity. Hosts without it use the terminal fallback, and the README says which. **Done on 2026-11-04:** the four CLIs show prompts inside tool calls and while idle, and their models see neither the prompt nor the answer; Antigravity cancels every prompt. The Claude desktop app's Code tab and the Codex desktop app are still to check.
9. The middle ground after the independent review: the cheap fixes are in this design, and the rest are the accepted limits above.
10. Antigravity is limited to local topics, because it cannot show prompts; the terminal fallback stays for the other hosts.

## Implementation outline

1. Confirm MCP elicitation in each host (done for the four CLIs and Antigravity; desktop apps pending; see "How prompts are handled").
2. MCP server: send requests to the client outside tool calls and match their replies, with Pigeon's own time limit and stale answers ignored.
3. Remove invitation pairing and topic sharing; drop their tables; peer protocol version 2.
4. Listed topics: `remote` on `pigeon_join` and `--remote`; listing to known machines; `pigeon_topics` and `pigeon topics on <machine>`.
5. Joins: the code exchange with commitment, first-contact confirmation, Allow/Deny for known machines, request limits, identity-changed handling, address updates on contact.
6. Terminal fallback: `pigeon requests`, `--show`, `allow`, `deny`.
7. Opt-in approval for local topics, and "who joined" notices. Refuse remote topics and opt-in approval for Antigravity sessions.
8. Defaults and transport: tidied host-name default, `pigeon peer listen` with no address, host names accepted, remaining lifetime instead of absolute expiry.
9. Skills in every plugin except Antigravity's: open a topic to other machines, list, join and disconnect. Antigravity's skill says remote topics are not available there.
10. Docs: README (with the single-user warning and per-host elicitation), [security design](security-design.md), [architecture](architecture.md), known issues, changelog.
11. Tests: two services in one test run (listing, codes right and wrong, Allow and Deny, expiry, limits, first contact, identity changed, disconnect, local approval), then the two-machine host test again.
