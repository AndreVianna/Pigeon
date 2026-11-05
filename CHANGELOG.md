# Changelog

The CLI and the Claude, Codex, Antigravity, OpenCode and Copilot plugins share one version (see the README's developer section).

## 0.6.0-alpha.3 (unreleased)

Connects agents on different machines on the same network, including other people's ([remote connections design](docs/remote-design.md)). A first version with invitations and per-topic sharing worked between two Windows machines on a home Wi-Fi network; this version replaces its handshake. Agents on two machines are not yet host-tested.

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
