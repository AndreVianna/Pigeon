# Changelog

The CLI and the Claude, Codex, Antigravity, OpenCode and Copilot plugins share one version (see the README's developer section).

## 0.7.0-beta.1 (unreleased)

An exploratory beta invitation for macOS and Linux testers. The installer, MCP connection, agent wake and recovery results are collected separately; agent integrations on those hosts are still unverified.

### Changed
- Development and CI now select the .NET 11 GA SDK (11.0.100) instead of RC3. The test projects use the updated stable Microsoft.NET.Test.Sdk and xUnit v3 packages, with Microsoft Testing Platform selected for `dotnet test`.
- The README, beta guide and issue form distinguish local-only defaults from opt-in peer connections and CLI messaging from a verified agent wake. Installation instructions no longer say that the self-contained installers require the .NET SDK.
- The prerelease includes `SHA256SUMS.txt` for the installer assets. It records download integrity but is not a code signature.

### Fixed
- The Claude hook wrapper validates wake output using shell built-ins, so Git Bash no longer needs separate `tr` and `grep` programs on `PATH`.

## 0.6.0-beta.1 (2026-11-08)

The first beta of 0.6.0, promoted by the user on 2026-11-07 after the two-machine host tests with agents (M1–M7, D1–D7): Claude Desktop on two Windows machines and Antigravity, with join codes in Pigeon's own windows. Together with alpha.1 to alpha.8 it adds macOS and Linux builds, the Antigravity, OpenCode and Copilot hosts, and agents on other machines of the same network.

Not yet tried: agents on macOS or Linux (including Pigeon's windows there), Tailscale, and the hosts' own prompts (Claude Code in a terminal, Codex, Copilot, OpenCode) across machines.

### Added
- **Installers for each system**, with no .NET needed: a per-user `.msi` for Windows, a `.pkg` for each Mac processor, and `.deb`, `.rpm` and `.tar.gz` for Linux (x64 and arm64). Each holds one self-contained, trimmed and compressed `pigeon` (about 18 MB) and registers no service. The **Installers** workflow builds them all, then installs and checks the Windows x64, Apple Silicon and Ubuntu x64 packages on their native runners; the other formats receive build or metadata checks. A `v*` tag makes a draft prerelease with them. They are not signed yet.
- A [beta testing guide](docs/beta-testing.md) and an issue form for beta reports.

### Changed
- **New default ports: 29372 for the local service and 29374 between machines** (were 49372 and 49374). The old ones were in the range Windows, macOS and Linux give to outgoing connections, and Windows reserves blocks of it for Hyper-V and WSL, so `pigeon start` or `pigeon peer listen` could fail with "access forbidden" after a restart (found in CI). After upgrading both machines from an alpha, run `pigeon peer address <name> <address>` on each, without a port, so it reaches the other on 29374; or forget the machine and join again. A port set with `PIGEON_PORT` or `pigeon peer listen <address>:<port>` is kept.
- Machines talk over TLS 1.3, or TLS 1.2 when one of them is a Mac: .NET on macOS accepts no TLS 1.3 connections, so a Mac could not listen (found in CI). Both versions still require both certificates and pinned fingerprints; under TLS 1.2 an observer on the network can tell which machines talk, but not read or change what they say.

### Fixed
- Forgetting a machine says that each shared topic stays on the machine that opened it, with a free seat; "ended on both machines" made an agent think the topics were gone (dialog host test D7, found on alpha.8).
- The plugins no longer describe Pigeon as limited to this computer.

## 0.6.0-alpha.8 (unreleased)

### Added
- An agent can forget a machine when its person asks: `pigeon_leave` with `machine` instead of `topic` does what `pigeon peer remove` does (every topic with that machine ends on both machines, which is told). It needs no prompt, since it only takes access away; joining again needs a join code. The connect skills say to do it only when the user asks.

### Fixed
- `pigeon_leave` no longer claims that leaving a topic with another machine ends it there too.

## 0.6.0-alpha.7 (unreleased)

Removes the terminal from approvals in hosts that cannot show Pigeon's prompts ([design](docs/remote-design.md), "Native dialogs").

### Added
- **Pigeon's own windows** for hosts that cannot show its prompts (the Claude desktop app, Antigravity, hosts without elicitation): Allow and Deny, a field for the join code, and the code to pass on with a Copy button, kept open until the join completes. Windows uses Windows PowerShell, macOS `osascript`, Linux `zenity` or `kdialog`. The text from the other machine is never put into a script. `PIGEON_PROMPTS=terminal` turns the windows off.

### Changed
- Antigravity sessions may open and join topics with other machines, and topics that require approval; they were limited to local topics.
- A local join that needs approval from a host that cannot show prompts says to answer in a terminal, as remote joins do.
- The connect skills say that leaving reports what happens to the topic on the other machine, instead of claiming it ends on both.

## 0.6.0-alpha.6 (unreleased)

A dev build from the two-machine test of alpha.4 (Claude Desktop on both machines).

### Fixed
- Removing a machine now tells it: a "forget" notice goes to its last address (kept for up to a day), and that machine ends the shared topics and forgets this one too. Before, the removed side kept the topic and retried its messages for a day.
- Topic lists (`pigeon topics` and the agents' `pigeon_topics`) say which topics are open to other machines.
- Leaving a topic on another machine explains that the topic stays there with a free seat; an agent had concluded it ended on both sides.
- A message to another machine is described as on its way (delivered within seconds while reachable), not as merely queued; agents reported every message as undelivered.
- A completed join counts as contact with the other machine in `pigeon peer list`.
- The Claude connect skill says that in the Claude desktop app join requests are answered in a terminal.

## 0.6.0-alpha.5 (unreleased)

A dev build for the two-machine test of alpha.4.

### Fixed
- Claude sessions in the Claude desktop app cancel Pigeon's prompts without showing them, so every first contact was refused. Pigeon now recognizes them (`CLAUDE_CODE_ENTRYPOINT=claude-desktop`) and leaves their prompts to the terminal (`pigeon requests`, `pigeon allow`, `pigeon deny`); the agent is told so.

## 0.6.0-alpha.4 (unreleased)

Connects agents on different machines on the same network, including other people's ([remote connections design](docs/remote-design.md)). It replaces the handshake of 0.6.0-alpha.3 (invitations to pair machines, then each topic shared by a person) with topics open to other machines and join codes. Agents on two machines are not yet host-tested.

### Changed
- Removed `pigeon peer invite`, `accept` and `confirm`, and `pigeon topic share` and `unshare`. Pending pairings from alpha.3 are dropped when the database is opened.

### Added
- **Topics open to other machines.** A session opens a topic with `remote: true` (`pigeon join … --remote`); another machine joins `<topic>@<address>` the first time and `<topic>@<machine>` after that. Only open topics with a free seat are listed, and only to known machines (`pigeon_topics` with `machine`, `pigeon topics on <machine>`).
- **Join codes.** The first join between two machines fixes a 6-digit code from random values both sides commit to in turn, and from both certificates. The requesting person confirms the contact and sees the code; the listing person enters it; then the machines pin each other's certificate. Joins from a known machine get Allow or Deny.
- **Prompts outside the conversation.** Pigeon's MCP server asks the person by MCP elicitation as soon as a request arrives, outside any tool call, so no model turn is spent. The model never sees a code. Every prompt has a 10-minute limit; no reply, Decline and Esc all mean Deny, and late or replayed answers are ignored. Checked in Claude Code, Codex, Copilot CLI and OpenCode with a probe (`experiments/elicitation-probe`).
- **Terminal fallback:** `pigeon requests [--show <request>]`, `pigeon allow <request> [<code>]` and `pigeon deny <request>`.
- **Opt-in approval for local topics** (`approval: true`, `--approval`): a local joiner gives a code in the same way. Pigeon tells the waiting session who joined.
- **Disconnecting:** leaving a remote topic ends it on both machines, and the other side is told.
- `pigeon peer listen` with no address listens on the machine's LAN and Tailscale addresses, never virtual adapters or public addresses. The machine name defaults to the tidied host name, and addresses may be host names.
- Each service has an ECDSA P-256 identity in the user's certificate store (`PIGEON_IDENTITY_DIR` keeps it in a folder instead, for tests). Machines talk over TLS 1.3 with mutual authentication; the listener serves only the peer protocol (version 2), with rate limits.
- Messages to another machine wait in an outbox and are retried with backoff. They carry their remaining lifetime, so clocks that differ do not matter, and each delivery updates the sender's address.

### Not available
- Antigravity sessions are limited to local topics: Antigravity advertises elicitation but cancels every prompt.

## 0.6.0-alpha.3 (unreleased)

Connects Pigeon services on different machines on the same network, including other people's ([remote connections design](docs/remote-design.md)). Tried on 2026-11-04 between two Windows machines on a home Wi-Fi network with the CLI at both ends. Its handshake was replaced in 0.6.0-alpha.4 before release.

### Added
- `pigeon peer` commands: name the service, open the peer listener on one chosen address (port 49374 by default), invite, accept and confirm with a 6-digit check code both people compare, list, remove, and update a peer's address.
- `pigeon topic share <topic> with <peer>` and `pigeon topic unshare <topic>`: pairing grants nothing until a person shares a topic; the peer's agent joins it as `<topic>@<peer>`.
- Each service has an ECDSA P-256 identity in the user's certificate store (`PIGEON_IDENTITY_DIR` keeps it in a folder instead, for tests). Peers talk over TLS 1.3 with mutual authentication and pinned fingerprints; the listener is off by default and serves only the peer protocol, with per-peer rate limits.
- Messages to another machine wait in an outbox and are retried with backoff until delivered or expired; a delivered message is stored once. A remote member appears as `@<peer>` with its tool name (unless withheld), never its session ID.
- `pigeon status` lists peers once they are used.

## 0.6.0-alpha.2 (unreleased)

Adds three hosts: the Antigravity App, host-tested in the Antigravity App 2.28 on Windows (A1–A8 in `experiments/host-test`), OpenCode 2, host-tested with OpenCode 2.2.5 on Windows (O1–O6), and GitHub Copilot, host-tested in VS Code 1.145 and the GitHub Copilot app on Windows (C1–C6).

### Added
- An Antigravity plugin (`integrations/antigravity-plugin`) with the MCP server, a `connect` skill, hooks and a **Pigeon wake** sidecar.
- Antigravity identifies the calling conversation in every MCP tool call, so its agents join without passing a session ID. One Pigeon MCP server can serve several Antigravity conversations, and each shows as connected.
- An idle Antigravity conversation is woken by the sidecar with `agentapi send-message`. A message sent during a turn would join that turn and derail the agent, so the plugin's hooks report each turn's start and end (`pigeon hook turn-start|turn-end antigravity`), and the service sends a wake only after a conversation has been idle for two seconds. A wake that starts no turn is retried after the wake retry time and gives up like any other.
- An OpenCode 2 plugin (`integrations/opencode-plugin/pigeon`). It registers Pigeon's MCP server, whose tools act for the session OpenCode names in each call (`_meta["ai.opencode/sessionID"]`), so OpenCode agents also join without a session ID. The plugin reports each session's turns to the service (OpenCode 2.2.5 publishes them to plugins as `session.execution.*` events) and wakes an idle session with a queued prompt, which runs after any turn in progress rather than joining it. Its wake logic has its own tests (`node --test`), run in CI.
- A GitHub Copilot plugin (`integrations/copilot-plugin`) for the Copilot runtime that the Copilot app, Copilot CLI and VS Code's Copilot chats share. The runtime starts one MCP server per session with `COPILOT_AGENT_SESSION_ID`, so Copilot agents join without a session ID (without it, as in VS Code's Local chats, the `vscode.conversationId` VS Code puts in each call is used). The plugin's Copilot extension, which the runtime starts for each session, reports the session's turns, wakes it with a queued prompt (`session.send`, `enqueue`) once it has been idle for two seconds, and allows Pigeon's own tools without a prompt, so a wake never waits for approval. Its tests run with `node --test` in CI.
- `pigeon status` shows whether the Antigravity sidecar is running. The sidecar stops when the Antigravity language server that started it exits, since Antigravity leaves its sidecars running when it quits.

### Changed
- Hook input may be up to 1 MiB (it was 8 KiB), since Antigravity's hook payloads can include the user's prompt. Pigeon still reads only the session ID.
- On Windows, hooks return at once when no Pigeon service is running, instead of after the 2 seconds a refused local connection takes there. The Antigravity turn hook runs before every model call.

## 0.6.0-alpha.1 (unreleased)

First step toward 0.6.0: Pigeon runs on macOS and Linux. Not yet tried with Claude or Codex on those systems.

### Added
- macOS and Linux support. Storage uses the system's SQLite library on every platform. The Claude hook waiter watches its launcher shells through `/proc` on Linux and `libproc` on macOS, as it does through process handles on Windows. `pigeon start` starts the service detached from the terminal, and it survives the terminal closing.
- CI runs the build and tests on Windows, Ubuntu and macOS.

### Fixed
- The Claude hook wrapper used bash-only syntax although the plugin runs it as `sh`. Under dash (`sh` on Debian and Ubuntu) it stopped with "Bad substitution" and exit code 2, which Claude Code treats as a wake. It now uses only POSIX features.

## 0.5.0 (2026-11-03)

First stable release. It contains the changes of 0.5.0-beta.1 and 0.5.0-beta.2 below, with no further code changes. Coming from 0.1.0-alpha.19 or the 0.4/0.3 plugins, read "Changed" under beta.1: topics are now private conversations between two sessions, and the commands and agent tools are new.

### Tested
- On 2026-11-03 the end-to-end host tests passed on beta.2 in Claude Desktop and Codex: a message wakes an idle Claude conversation; Claude and Codex exchange three rounds with no typing; a renewal after idle time re-arms the waiter; a killed hook shell orphans no waiter; one wake delivers messages from two topics; a third session is refused from a full topic. The 12-hour renewal, Codex recovery after close and restart, and Claude Desktop reopen were reported as passed by the repository owner. See the [host-test runbook](experiments/host-test/README.md).

### Known limits
- No application-level authentication; a session ID is a routing claim.
- Delivery is at least once; duplicates are possible.
- A conversation whose process the host has not started (after a reopen or app restart) cannot be woken until it is used again.
- A Codex chat whose Pigeon MCP server exits (for example when the service is stopped for an update) needs to be reopened, or the app restarted.
- Each idle Claude conversation uses a short renewal turn every 12 hours.
- Built on .NET 11 RC 3; it needs the matching ASP.NET Core runtime.

See [known issues](docs/known-issues.md) for the full list.

## 0.5.0-beta.2 (pre-release)

### Fixed
- Cursor imports installed Claude plugins' hooks and runs them through PowerShell, where the Claude plugin's bare `hook-wait.sh` command opened the script in a text editor at every chat start and turn end. The plugin now runs it as `sh "…/hook-wait.sh"`: unchanged in Claude Code's Git Bash, and a harmless "not found" in other shells. (`bash` was avoided because on Windows it often resolves to WSL.) A unit test keeps the command in this form.

## 0.5.0-beta.1 (pre-release)

First beta. The CLI moves from 0.1.0-alpha.19, the Claude plugin from 0.4.0 and the Codex plugin from 0.3.0 to one shared version. This release changes how Pigeon is used; see "Changed".

### Changed
- **A topic is a private conversation between two sessions.** The first session to join creates it and waits for a partner, the second completes it, and a third is refused with both members named. Messages go to the other member and queue in order, also while the other seat is empty. A leaving member's unread messages pass to its replacement, the last member to leave deletes the topic, and a topic whose members have both been away for an hour is deleted.
- **New commands:** `pigeon join <topic> as <session-id> using <tool>`, `pigeon leave <topic> as <session-id>`, `pigeon send from <session-id> to <topic> "<message>"`, `pigeon receive for <session-id> from <topic>` and `pigeon topics`. A session is identified by its session ID alone; the tool is given once, when joining.
- **Seven agent tools:** `pigeon_join`, `pigeon_leave`, `pigeon_topics`, `pigeon_send`, `pigeon_receive`, `pigeon_wake_complete` and, for the hooks, `pigeon_session_restore`. `pigeon_receive` confirms a wake and returns its messages from all of a session's topics; completing the wake acknowledges them.
- `send` reports whether the recipient is connected, away or not yet in the topic.
- A topic accepts up to 60 messages an hour (`PIGEON_MAX_MESSAGES_PER_HOUR`), replacing the automated-exchange limits.
- The plugins' hooks re-arm a wake that gave up when a session returns.
- Databases from earlier versions are upgraded on first open, keeping each session's topics with up to two members and their pending messages.

### Removed
- Name-based agents, multi-member topics, direct session-to-session sends, topic history, the activity log, pause, explicit acknowledgment, delivery status, correlation and reply IDs, caller-chosen expiry and retry keys.
- The `messages`, `sessions`, `topics create/delete/join/leave/sessions/subscribe/unsubscribe` commands, `messages wait` (the Claude monitor) and the Claude channel mode (`pigeon mcp --claude-channel`).
- `PIGEON_MAX_AUTOMATED`, `PIGEON_MAX_AUTOMATED_PER_MINUTE` and `PIGEON_MESSAGE_TTL_SECONDS`.

### Fixed
- A busy or locked database is reported as "storage unavailable" instead of a generic error; the SQLite busy timeout (2 s) is shorter than the client timeout, and a rollback runs only inside a transaction.
- A new message re-arms an exhausted wake.
- Tool names are compared consistently, so sessions registered as "Claude Code" are the same as `claude-code`.
- The Claude hook waiter tracks its whole launcher chain, so killing its outer Git Bash shell stops it before it can claim a wake into a closed pipe.
- A hook waiter that owned its session still requests its renewal when the service is unavailable at the deadline.
- The Codex plugin now loads its MCP server and hooks: it uses the `.codex-plugin/plugin.json` layout with a `.mcp.json`, which Codex requires for local servers.

### Added
- `pigeon status` shows the service version, warns when it differs from the CLI, and flags Claude sessions with no notifier since the service started.
- The repository is a plugin marketplace named `pigeon` for both Claude Code (`.claude-plugin/marketplace.json`) and Codex (`.agents/plugins/marketplace.json`).
- Command errors explain what went wrong (for example which sessions fill a topic) and exit with 1; usage errors exit with 2.

### Known limits (at beta.1; see 0.5.0 above for the current list)
- No application-level authentication; a session ID is a routing claim.
- Delivery is at least once; duplicates are possible.
- A conversation whose process the host has not started (after a reopen or app restart) cannot be woken.
- On Claude Desktop the hook waiter's chain ends at its top Git Bash shell, so it does not notice a conversation process that exits while those shells survive; it stops at its deadline.
- The 12-hour hook renewal has not yet run on the host, and the two-member redesign has not yet run in Claude Desktop or Codex.

See [known issues](docs/known-issues.md) for the full list.
