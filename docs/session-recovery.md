# Tool/session routing and recovery

## Identity and migration

A session is identified by its session ID. The tool it belongs to is recorded when it first joins and cannot change later; tool names are normalized to lowercase, with underscores and spaces changed to hyphens, so `Claude` and `claude-code` are the same tool. Session IDs are exact claims; no routing claim authenticates its caller.

A session joins a topic with `pigeon_join` or `pigeon join`, and can be in several topics. Restoring a known session rebinds it and re-arms a wake that gave up, but never creates a session or topic. A session that was never joined stays unknown.

Databases from earlier versions are upgraded on first open. Sessions that had a tool/session route keep it, each topic keeps up to two of its members (the most recently active), and their pending messages are carried over. Name-based agents and the earlier tables are left in the file but no longer used; members that did not fit are recorded under `upgrade_removed_members` in the database's metadata table.

## Hook and bridge lifecycle

The plugins contain `SessionStart` command hooks that read only `session_id` from the host's input. They do not read a transcript, create a topic, launch a service, or bind an MCP connection. The Claude hook waits for a wake for an existing association; its `Stop` hook starts another waiter after a turn. Claude's SessionStart MCP hooks are unavailable at process launch, and Codex can run them before an MCP server is ready. Each plugin also supplies a `UserPromptSubmit` MCP hook as a later bridge-binding fallback. These hooks are opt-in through plugin installation, and Codex requires review and trust of the exact hook definitions.

The Claude plugin opts its MCP process into restoration with `PIGEON_RUNTIME=claude-code`. When its host provides `CLAUDE_CODE_SESSION_ID`, the bridge tries to bind the saved pair as soon as it is initialized, without waiting for a user prompt. If the service is offline, it keeps the known restore intent in memory and retries while the bridge remains running. If the host omits the ID, the bridge reports `missing-session`; the prompt hook can try later. An unknown session stays unbound; a restore the service rejects reports `conflict`. A `bound` status describes a routing claim, not host authentication or notification delivery.

An isolated Codex app-server did not pass `CODEX_THREAD_ID` or `CODEX_SESSION_ID` to its MCP child. Pigeon intentionally does not guess based on process ancestry or an inherited unrelated ID. The Codex plugin attempts an MCP restore using the hook event's session ID at startup if the MCP server is ready, then on user prompts. Its exact host server name, hook trust flow, and no-prompt Desktop recovery remain unverified.

Registration is separate from notification. The Claude hook waiter can wake a running conversation; no Monitor is needed. After 12 idle hours an active waiter requests a renewal model turn, allowing Stop to arm another. Renewal requires the service and host to be available at the deadline. Pigeon cannot guarantee that Desktop starts a reopened conversation's process or that the host delivers a renewal or arms its replacement. A message is acknowledged only when the agent completes the wake that received it.

## Evidence

The Gherkin integration scenarios use disposable SQLite databases and real local MCP/CLI processes to cover two-member topics, held and replaced messages, restore, upgrades from earlier databases, hook wakes and notifier ownership, along with wake coalescing, bounded retries, expiry, retry keys and orphaned-waiter safeguards. See [the hook waiter report](history/claude-hook-waiter-report.md) for isolated Desktop test results; those used a development plugin, not this production package.

An approved isolated Claude Code `--print`/stream-JSON probe used two successive nonpersistent processes with the same synthetic session ID and a disposable service. The MCP child received the synthetic ID, and the opted-in bridge bound the saved route before either first prompt. A separate run removed the ID before the bridge started; the `UserPromptSubmit` MCP hook bound it after the first prompt in each process. The host emitted successful `SessionStart:startup` hook events. These tests do not resume a persisted conversation and do not establish Claude Desktop Monitor startup.

An approved isolated Codex app-server probe used one new ephemeral thread, a disposable service, and hooks disabled for the environment check. Neither `CODEX_THREAD_ID` nor `CODEX_SESSION_ID` was present in its MCP child, and bridge auto-restore did not occur. Codex plugin hooks were not executed in that probe. The unit/integration hook tests simulate host event input; they are not a real Codex Desktop hook-trust or resume test.

The isolated probes did not access Sigma sessions, modify global agent configuration, or install a background service. Do not infer guaranteed Desktop app restart or automatic wake behavior from those probes.
