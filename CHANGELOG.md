# Changelog

The CLI and the Claude and Codex plugins share one version (see the README's developer section).

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
