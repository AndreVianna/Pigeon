# Pigeon

Pigeon is a local Windows messaging service for agent sessions. **A topic represents the project the sessions are working on.** One service can manage many topics at the same time. Each session is routed by its **tool and session ID**; messages to a topic are queued for its other subscribers. A session can also read a topic's retained posts without subscribing.

The MVP has no application-level authentication. Any local process that can reach the service, including processes running under other local accounts, can read messages, impersonate a routing name or stop the service. The service binds only to IPv4 loopback. Peer messages are untrusted data and do not confer authority or file ownership.

## Start and use the service

```powershell
pigeon start
pigeon status
pigeon topics create pigeon
pigeon topics join pigeon codex <codex-session-id>
pigeon topics join pigeon claude-code <claude-session-id>
pigeon topics sessions pigeon
pigeon messages send-session codex <codex-session-id> claude-code <claude-session-id> "Hello from Codex" --topic pigeon
pigeon messages get-session claude-code <claude-session-id> pigeon
pigeon messages ack-session claude-code <claude-session-id> <receipt>
pigeon stop
```

Substitute real session IDs and receipts; the angle-bracket placeholders are not literal PowerShell arguments. A registration records a claim, not proof that a runtime is connected. `pigeon start --foreground` keeps the service attached to the terminal. `PIGEON_PORT` optionally selects the loopback port (default `49372`) and `PIGEON_DATABASE` an absolute database path; neither selects a project. The default database is `%LOCALAPPDATA%\Pigeon\messages.db`. The .NET 11 ASP.NET Core runtime is required. No Windows service, firewall rule or global agent configuration is installed by these commands.

`pigeon status` reports the service version (and warns if it differs from the CLI's), recently active MCP bridges and their claimed identities, subscribed topics and wake states, plus up to 100 recipients with pending messages even when disconnected. A heartbeat indicates the bridge can reach Pigeon; it does not prove the agent is processing a message or that Claude has accepted its channel. Heartbeats occur every three seconds and expire after 15 seconds. Graceful disconnect removes the bridge; a service restart loses the bridge list until bridges heartbeat again. Wake state and attempt history are stored durably.

## Connecting Codex and Claude Code

Configure Pigeon as a stdio MCP server in each tool. No project, topic, or agent environment variable is required. The included [Codex](integrations/codex-plugin/.mcp.json) plugin opts in to queue notifications; the [Claude Code](integrations/claude-plugin/.mcp.json) plugin uses ordinary MCP and hook-based notification. They are not installed automatically; changing agent configuration requires the approvals previously specified.

Install each as a plugin, not as a bare MCP server, so its hooks and connect skill are active:

- **Claude Code:** the marketplace is [`integrations/.claude-plugin/marketplace.json`](integrations/.claude-plugin/marketplace.json) (`pigeon-local`, plugin `pigeon`).
- **Codex:** the repo marketplace is [`.agents/plugins/marketplace.json`](.agents/plugins/marketplace.json) (`pigeon-local`, plugin `pigeon`, source `./integrations/codex-plugin`). Add it with `codex plugin marketplace add <repo root>` or open the repo in the ChatGPT desktop app, install Pigeon from the Plugins directory, and review its hooks. Codex starts a plugin's local MCP server only from the `.codex-plugin/plugin.json` layout with a `.mcp.json`, which this plugin uses since 0.3.0; after updating from 0.2.0, uninstall and reinstall it so the server and hooks load. Remove any standalone `pigeon` MCP server first so two servers with the same name are not configured.

Each session calls `pigeon_session_join` with `topic`, `tool` (`codex` or `claude-code`) and its real `sessionId`. The tool/session pair identifies the routing destination and is subscribed to the topic atomically. For Codex queue delivery, use the actual target thread ID; a session-tree root ID can differ from a forked thread ID. A session can join another topic with the same identity. Session IDs are claims; Pigeon does not authenticate them or discover existing sessions. The former `pigeon_topic_join`, `pigeon_register` and name-based commands remain available for existing integrations.

Useful MCP tools are `pigeon_topics`, `pigeon_topic_create`, `pigeon_session_join`, `pigeon_session_restore`, `pigeon_topic_sessions`, `pigeon_send_session`, `pigeon_publish`, `pigeon_receive`, `pigeon_acknowledge`, `pigeon_topic_read`, `pigeon_subscribe` and `pigeon_topic_delete`. `pigeon_topic_sessions` returns the tool/session pairs subscribed to a topic. Direct sends use `recipientTool` and `recipientSessionId`; received messages expose `SenderTool` and `SenderSessionId`. For wakes, use `pigeon_wake_confirm`, `pigeon_wake_complete` and `pigeon_wake_status`. Pigeon generates a correlation ID, idempotency key and 24-hour expiry when omitted. For reliable retries, specify a fixed correlation ID, idempotency key and expiry. A returned delivery ID or channel transport write is not an acknowledgment: the receiving agent must explicitly acknowledge its receipt.

### Restoring a session

The plugins include `SessionStart` command hooks and MCP restore hooks. The MCP bridge binds to a saved tool/session association without creating a topic. The Claude plugin opts its MCP process into automatic restore using `PIGEON_RUNTIME=claude-code` and the session ID supplied by the host. If the ID is missing, a `UserPromptSubmit` MCP hook attempts restoration on a later prompt. If a known restore request encounters a stopped service, the bridge retries while it remains running. A new, never-joined session is not registered automatically.

The isolated Codex app-server did not supply a thread ID to its MCP child. Pigeon therefore does not guess one from a possibly inherited environment. Its plugin uses the hook-provided session ID to attempt an MCP restore at startup when the server is ready and again on a user prompt. Codex requires explicit trust review for plugin hooks; the hook's exact server name and behavior in Codex Desktop have not yet been validated. `pigeon status` reports `restore: bound`, `pending`, `missing-session`, `not-configured`, or `conflict` when the bridge supports that status. `bound` is a routing association, not proof of notification or runtime liveness.

The Claude plugin's hooks also start a bounded wake waiter when the host runs them. Notification after reopening depends on Desktop starting the conversation process; it is not guaranteed. See [session recovery design and test evidence](docs/session-recovery.md).

For Codex, start its MCP bridge with `pigeon mcp --codex-queue` to attempt safe-checkpoint notifications via `codex queue` for the registered session ID. This sends a wake generation and a fixed request to check Pigeon; it does not inject the peer's message body or acknowledge it. The command must be available to the bridge and able to reach the session's owning runtime. Queue-command acceptance does not prove runtime receipt. An alpha.13 isolated idle persistent test thread received a wake through its own MCP bridge, confirmed its generation and acknowledged its message. A separate ephemeral test thread recorded a transport failure; in another isolated ephemeral probe, `codex queue` reported that the thread or session was not found. Do not assume ephemeral sessions are reachable. Busy, restarted and independently owned desktop sessions remain unverified with alpha.13.

For Claude Code, configure its MCP command as `pigeon mcp --claude-channel` and explicitly load that channel in the interactive session. For a bare MCP server named `pigeon`, Anthropic's research-preview development path is `claude --dangerously-load-development-channels server:pigeon` and requires confirming Claude's warning. Account or organization policy can block channels. The channel must be loaded at session startup; changing an MCP registration alone does not enable it in an existing session. It writes a notification without acknowledging the delivery. An alpha.13 isolated idle Claude session confirmed its wake generation and explicitly acknowledged a synthetic message; busy and restarted sessions remain unverified with real agents. See [Claude Code channels](https://code.claude.com/docs/en/channels-reference).

Both paths are opt-in. A subscription alone does not load a notification channel or attach to a running session. Without notification, agents can poll.

### Claude Desktop notifications

The bundled Claude plugin includes `/pigeon:connect` for joining a topic. Its `SessionStart` and `Stop` hooks run `pigeon hook wait claude-code` in the background for an associated session. When a wake is due, the waiter sends a fixed instruction to the host and exits; the agent confirms the generation, receives and acknowledges messages, then completes the wake. The waiter never reads message bodies. The Stop hook starts a new waiter after a turn. Install or update the local plugin separately from the CLI; plugin configuration changes are not automatic. The local marketplace is `integrations/.claude-plugin/marketplace.json` and its path must remain available.

The plugin's hook wrapper runs under Git Bash and filters incompatible CLI errors so they cannot become repeated model wakes. Update the CLI and restart the service before updating the plugin. If the installed CLI does not support the hook's arguments, the wrapper remains silent and notification is unavailable; inspect `pigeon --version` and `pigeon status`.

The service, MCP bridge, and hook waiter must use the same `PIGEON_PORT`. For a non-default port, ensure the hook process inherits that setting as well; setting it only in the MCP server's environment does not configure the hooks.

Only one Claude notifier owns a session's wake claims at a time. `pigeon status` shows the current owner or its last release; that is not proof that the agent received or processed a message. A stopped notifier may take up to 15 seconds to lose ownership. Avoid registering a channel bridge that the host has not actually loaded, since it could own notifications without delivering them.

The production hook waiter requests a short renewal turn after 12 idle hours; the hook timeout is 12 hours and two minutes. The renewal is not a message: Claude should end the turn so the Stop hook starts a replacement. It uses the session's model and can cost up to two model turns per day per connected idle session. Desktop has not yet been verified to honour this 12-hour timeout. A renewal depends on the service, the host delivering the wake and the host running Stop; if any fails at the deadline, messages remain queued until another turn or supported session-start event. Reopening a Desktop conversation does not guarantee that Desktop starts its process. A waiter cannot wake a process that is not running. Polling through MCP remains available.

An isolated development plugin was tested in Claude Desktop with synthetic Pigeon messages, including idle and busy turns, bursts, service restart, notifier takeover, and selected session and app reopen cases. These observations do not establish reliable operation in every host or restart state. The production plugin and packaged release have not yet been tested in a live Desktop session. See [the test report](docs/history/claude-hook-waiter-report.md) for details and limits.

## Durable wake protocol

Pigeon stores one wake generation for each recipient with pending messages. A burst shares a generation; a wake tells the agent to call `pigeon_wake_confirm` with that generation. If confirmation returns `Accepted: false`, the wake is stale and the agent should stop. If it is true, the agent may receive and process a bounded batch, explicitly acknowledge each receipt, then call `pigeon_wake_complete`. Confirming or completing a wake never acknowledges a message. If work remains, Pigeon creates another generation when appropriate. A delayed wake can still appear as a Codex prompt, but cannot confirm a completed or superseded generation.

Wake attempts are persisted independently of message delivery attempts. `pigeon_wake_status` shows state and the latest 20 wake attempts; `pigeon status` also reports pending recipients and their wake states. `ready` means pending work; `dispatching` means a bridge claimed an attempt; `sent` means the transport accepted or wrote it; `failed` means the transport reported failure or a dispatch outcome was lost; `confirmed` means the agent called the confirmation tool; `exhausted` means the wake retry budget was reached; `completed` means no queued or leased messages remain; `idle` means the recipient has never had a wake. These states are not proof of session identity or channel availability.

By default, a wake is retried at most three times with a 120-second interval. Configure `PIGEON_MAX_WAKE_ATTEMPTS` and `PIGEON_WAKE_RETRY_SECONDS` on the service. A crash during dispatch is recorded as an unknown transport outcome when retried. If wake attempts are exhausted, messages remain in their ordinary queue until acknowledged, expired or delivery retries fail; the next new message starts one fresh generation with a new attempt budget, which a burst shares; the operator can re-arm notification with `pigeon messages retry-wake <reader>`. Retrying or restarting may leave duplicate prompts already accepted by the runtime; exactly-once wake delivery is not guaranteed. Manual polling can drain the inbox without a wake confirmation. A temporary storage failure, such as another process holding the database lock for more than two seconds, is reported as `storage unavailable`; bridges, hook waiters and session restore treat it like a service outage and retry.

## Topics and delivery

`pigeon topics` lists all project topics. Creating an existing topic returns a warning. Subscribing to a missing topic fails. Unsubscribing does not discard already queued deliveries. Deleting a topic unsubscribes its members and queues a closure notice for each; pending messages are retained. A queued notice is not proof of receipt, and it expires after seven days.

A topic post creates a separate delivery for each current subscriber except the sender. Posts with no recipients are still stored. `pigeon_topic_read` returns up to three unexpired, unredacted posts in sequence order, including for a registered reader that is not subscribed. Pass the last `Sequence` as `afterSequence` for the next page. A reader's own posts are excluded. History reads do not lease or acknowledge deliveries. `pigeon messages get <reader> [topic]` leases up to 20 deliveries; acknowledge each receipt after processing.

Direct messages require sender and recipient to share a topic. If they share multiple topics, supply `--topic <name>` to `pigeon messages send-session` or `topic` to `pigeon_send_session`. For a direct reply, include the original correlation ID and reply-to ID. Unacknowledged leases can be retried, so duplicates are possible; there is no exactly-once guarantee. Automated exchanges, retries and expiry are bounded. The MCP pause control pauses exchange across all topics.

## Existing data and migration

Older Pigeon versions stored a separate project namespace above topics. On first startup, Pigeon moves existing records into the shared topic space in one SQLite transaction, retaining message IDs, deliveries and history. If two old namespaces contain the same topic name or agent name, Pigeon refuses to merge them automatically and leaves the legacy records unchanged. Run `pigeon start --foreground` to see the migration error; do not delete the database. Stop an older Pigeon service before starting the updated version, and restart MCP clients after updating: alpha.13 replaces the old notification protocol, so an old bridge cannot issue wakes through the new service. The new version has not been tested against your actual database.

The session routing upgrade adds a durable mapping without rewriting message IDs or moving history. A tool/session pair matching exactly one old name is adopted lazily. If multiple old names claim the same pair, automatic restoration reports a conflict. Inspect them with `pigeon sessions candidates <tool> <session-id>` and explicitly select one with `pigeon sessions adopt <tool> <session-id> <legacy-name>`. This selects the old mailbox for the new route; **other old mailboxes, subscriptions, and messages remain separate and are not merged or deleted**. Use the legacy name-based commands to inspect or drain them. Do not assume pending messages in another old mailbox were transferred.

## Build and test

The solution is `Pigeon.slnx`, targets .NET 11 and C# 14, and contains separate xUnit unit tests and Gherkin/Reqnroll integration tests.
The Claude hook wrapper integration test requires Git Bash; if it is not installed at its usual Windows location, set `PIGEON_TEST_BASH` to the Bash executable for the test run.

Current reliability limitations and follow-up work are documented in [known issues and technical debt](docs/known-issues.md), and proposed work in [next steps](docs/next-steps.md). The current design is described in [architecture](docs/architecture.md), [session recovery](docs/session-recovery.md) and [local trust boundaries](docs/security-design.md). Dated research, plans, test reports and handoffs are kept in [docs/history](docs/history) as evidence; they may describe superseded behavior. Host probes and test fixtures are indexed in [experiments](experiments/README.md).

```powershell
dotnet build Pigeon.slnx
dotnet test Pigeon.slnx
dotnet pack src/Pigeon.Cli/Pigeon.Cli.csproj --no-restore --output artifacts
```

Packing does not install the tool. For an earlier user-installed version, after you choose to update it:

```powershell
dotnet tool update --global Pigeon.Cli --version 0.1.0-alpha.19 --source .\artifacts
```

With `PIGEON_BODY_RETENTION_DAYS` configured, terminal message bodies are lazily redacted after the retention period, subject to publication sibling deliveries. Metadata and deduplication records remain; SQLite pages, WAL files and backups can retain data. This is not secure erasure. The other bounded service settings are `PIGEON_MAX_ATTEMPTS`, `PIGEON_MAX_AUTOMATED` and `PIGEON_MAX_AUTOMATED_PER_MINUTE`. `PIGEON_MESSAGE_TTL_SECONDS` applies only to the CLI's `messages send` and `messages direct` commands; MCP sends and `messages send-session` default to 24 hours.
