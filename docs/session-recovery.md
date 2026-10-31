# Tool/session routing and recovery

## Identity and migration

The public routing key is `(tool, sessionId)`. Tool names are normalized to lowercase, with underscores and spaces changed to hyphens; `Claude` and `claude-code` identify the same tool. Session IDs are treated as exact claims. The store uses an internal key so existing message, receipt, publication and wake references do not have to be rewritten. No routing claim authenticates its caller.

A session joins a topic with `pigeon_session_join` or `pigeon topics join`. It can have multiple explicit subscriptions. `pigeon_topic_sessions` lists subscribed tool/session pairs; `pigeon_send_session` addresses one pair. The existing name-based tools remain for older clients. A direct message still requires a shared topic. New messages and topic history include sender tool and session fields; old aliases remain readable.

When a pair matches exactly one old name, Pigeon creates a durable mapping to that old mailbox lazily. If several old names claim the same pair, it reports a conflict and does not choose, merge, or move them. Inspect `pigeon sessions candidates <tool> <session-id>` and explicitly select one with `pigeon sessions adopt <tool> <session-id> <legacy-name>`. Selection leaves every other mailbox, message and subscription intact. Messages in those other mailboxes are not automatically transferred; inspect or drain them using the legacy name-based interface. Once selected, a different name cannot silently replace the route.

Topic deletion removes subscriptions and enqueues closure notices. Restoring a known pair never recreates a topic or subscription. A new pair is not created by a restore attempt. A new legacy name cannot be registered against a pair already claimed by a different identity.

## Hook and bridge lifecycle

The plugins contain `SessionStart` command hooks that read only `session_id` from the host's input. They do not read a transcript, create a topic, launch a service, or bind an MCP connection. The Claude hook waits for a wake for an existing association; its `Stop` hook starts another waiter after a turn. Claude's SessionStart MCP hooks are unavailable at process launch, and Codex can run them before an MCP server is ready. Each plugin also supplies a `UserPromptSubmit` MCP hook as a later bridge-binding fallback. These hooks are opt-in through plugin installation, and Codex requires review and trust of the exact hook definitions.

The Claude plugin opts its MCP process into restoration with `PIGEON_RUNTIME=claude-code`. When its host provides `CLAUDE_CODE_SESSION_ID`, the bridge tries to bind the saved pair as soon as it is initialized, without waiting for a user prompt. If the service is offline, it keeps the known restore intent in memory and retries while the bridge remains running. If the host omits the ID, the bridge reports `missing-session`; the prompt hook can try later. An unknown pair stays unregistered. An ambiguous legacy pair reports `conflict`. A `bound` status describes a routing claim, not host authentication or notification delivery.

An isolated Codex app-server did not pass `CODEX_THREAD_ID` or `CODEX_SESSION_ID` to its MCP child. Pigeon intentionally does not guess based on process ancestry or an inherited unrelated ID. The Codex plugin attempts an MCP restore using the hook event's session ID at startup if the MCP server is ready, then on user prompts. Its exact host server name, hook trust flow, and no-prompt Desktop recovery remain unverified.

Registration is separate from notification. The Claude hook waiter can wake a running conversation without starting its Monitor tool. After 12 idle hours an active waiter requests a renewal model turn, allowing Stop to arm another. Renewal requires the service and host to be available at the deadline. Pigeon cannot guarantee that Desktop starts a reopened conversation's process or that the host delivers a renewal or arms its replacement. The service does not claim a message is acknowledged until the agent explicitly acknowledges its receipt.

## Evidence

The Gherkin integration scenarios use disposable SQLite databases and real local MCP/CLI processes to cover separate same-tool sessions and topics, direct routing, recovery, legacy adoption, ambiguous conflicts, hook wakes and notifier ownership. Existing tests cover duplicate delivery, wake coalescing, bounded retries, expiry, acknowledgment, offline recipients and orphan-monitor safeguards. See [the hook waiter report](history/claude-hook-waiter-report.md) for isolated Desktop test results; those used a development plugin, not this production package.

An approved isolated Claude Code `--print`/stream-JSON probe used two successive nonpersistent processes with the same synthetic session ID and a disposable service. The MCP child received the synthetic ID, and the opted-in bridge bound the saved route before either first prompt. A separate run removed the ID before the bridge started; the `UserPromptSubmit` MCP hook bound it after the first prompt in each process. The host emitted successful `SessionStart:startup` hook events. These tests do not resume a persisted conversation and do not establish Claude Desktop Monitor startup.

An approved isolated Codex app-server probe used one new ephemeral thread, a disposable service, and hooks disabled for the environment check. Neither `CODEX_THREAD_ID` nor `CODEX_SESSION_ID` was present in its MCP child, and bridge auto-restore did not occur. Codex plugin hooks were not executed in that probe. The unit/integration hook tests simulate host event input; they are not a real Codex Desktop hook-trust or resume test.

The isolated probes did not access Sigma sessions, modify global agent configuration, or install a background service. Do not infer guaranteed Desktop app restart or automatic wake behavior from those probes.
