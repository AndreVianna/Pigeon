# Host tests: runbook

Live tests from [the release plan](../../docs/release-plan.md), Phase 3. They check a frozen build in the real Claude Desktop and Codex against an isolated service on port 49473 with a disposable database, using new test conversations and synthetic messages. Your normal service on port 49372 and your existing conversations are not used.

## 0.5.0-beta.2 end-to-end tests (E1–E6)

Run on 2026-11-03; results below. They repeat the earlier host tests with the two-member topics and the new agent tools (`pigeon_join`, `pigeon_send`, `pigeon_receive`, `pigeon_wake_complete`), and with the hook wrapper now started through `sh`.

### Results (2026-11-03)

Run with the frozen Release build of `f77aab0` (0.5.0-beta.2), the dev plugins 0.5.0-beta.2.dev.1, Claude Desktop (Claude Code 2.1.309) and the Codex app, in one new Claude test conversation (`94260bcf-…`) and one new Codex chat (`01a1a254-…`).

| Test | Result | Evidence |
|---|---|---|
| E1 | Pass | Sent 11:44:52; the conversation woke by itself, received and completed the wake, and "E1 pong" reached the peer 6 s later. |
| E2 | Pass | Round 1 (Claude) 11:54:52 woke Codex through its queue; round 2 (Codex) 11:55:04 woke Claude through the hook waiter; round 3 (Claude) 11:55:08 woke Codex, which acknowledged and stopped. Each message was delivered once and acknowledged on completion; no typing after the first prompt. |
| E3 | Pass, one visual check pending | Waiter armed 11:45:16; renewal after 3 idle minutes and a replacement waiter at 11:48:14; "E3 after renewal" sent 11:48:19 and "E3 pong" arrived 11:48:24. Whether the renewal turn ended without polling or replying still needs a look at the conversation. |
| E4 | Pass | Killed only the outer hook `bash.exe` at 11:48:51; status showed `orphaned` at 11:48:54 and no waiter remained. "E4 after kill" waited with 0 wake attempts; after the user's next prompt the waiter re-armed and "E4 pong" arrived at 11:52:06. |
| E5 | Pass | One message on each of Claude's two topics (11:56:06); both were received and acknowledged under one wake generation within 4 s. The second message was sent through the CLI under the Codex test session's ID. |
| E6 | Pass | Codex's `pigeon_join` on the full `pigeon-host-test` was refused; the error named `host-test-peer` and the Claude session, and the topic kept its two members. |

**H4, H5 and H6** (the 12-hour renewal, Codex recovery and Claude reopen) were run by the user and reported as passed on 2026-11-03. Their logs are not in this repository.

**Finding.** With the hook started as `sh "…/hook-wait.sh"`, the waiter's launcher chain now reaches Claude Code: `pigeon.exe <- sh.exe <- sh.exe <- sh.exe <- bash.exe <- bash.exe <- claude.exe`. With the earlier direct command, the launcher above the hook shells exited at once and the chain ended at the top shell, so the waiter did not watch `claude.exe`. The waiter now also stops if the conversation process exits while its hook shells survive (not tested directly).

### What is prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Release build of `f77aab0`, version 0.5.0-beta.2. Rebuilding the solution does not touch it. |
| Claude dev plugin `pigeon-dev` 0.5.0-beta.2.dev.1 | `experiments/claude-hook-waiter-dev` | Hooks generated from production beta.2 (`sh "…/hook-wait.sh"`), with port 49473, the frozen build and a 3-minute renewal (hook timeout 5 minutes). Skill text is production's plus a dev header. |
| Codex dev plugin `pigeon-dev` 0.5.0-beta.2.dev.1 | marketplace `pigeon-codex-dev` at `experiments/codex-plugin-dev` | MCP server `pigeon-dev` (queue mode) on port 49473 with the frozen build, and the MCP restore hooks. |
| Helper | `experiments/host-test/hosttest.py` | `start` joins a scripted peer (`host-test-peer`) to topic `pigeon-host-test`; `send <text>` sends from it; `inbox` receives its replies; `status`, `waiters`, `kill-outer-shell`, `stop`. |

### Setup (needs your approval)

1. **Start the isolated service:** `python -I experiments/host-test/hosttest.py start`. It runs in the background until stopped.
2. **Claude test folder:** create the empty folder `C:\Projects\PigeonHookWaiterTest`, and from it:
   - `claude plugin marketplace add "C:\Projects\Pigeon\experiments\claude-hook-waiter-dev" --scope local`
   - `claude plugin install pigeon-dev@pigeon-hook-waiter-dev --scope local`
   - `claude plugin disable pigeon@pigeon --scope local`, so the production plugin (installed for your user) does not also load in the test conversation.
3. **You:** open a new Claude Desktop conversation in `C:\Projects\PigeonHookWaiterTest` and type `/pigeon-dev:connect pigeon-host-test`.
4. **You, for E2 and E6:** in Codex add the marketplace `C:\Projects\Pigeon\experiments\codex-plugin-dev`, install `pigeon-dev`, accept its hooks, and open a new chat. Tell it to use the **pigeon-dev** tools (your production `pigeon` plugin is also loaded there). Like any Codex plugin it loads in every chat; remove it after the tests.

### Tests

Run the helper from the repository root as `python -I experiments/host-test/hosttest.py <command>`.

| # | Test | Steps | Pass when |
|---|---|---|---|
| E1 | Claude wakes for a message (about 5 min) | After the connect turn: `status` shows the Claude session in `pigeon-host-test` with `notification: claude-hook`. Then `send "E1 ping: reply E1 pong on this topic."` | The conversation wakes by itself, calls `pigeon_receive` and `pigeon_wake_complete`, and replies with `pigeon_send`; `inbox` shows "E1 pong"; `status` shows nothing waiting. |
| E2 | Claude ↔ Codex, three rounds (about 10 min) | Codex chat: *"Use the pigeon-dev tools to join topic pigeon-host-test-pair. You may reply to Pigeon messages on that topic without asking me; replies only."* Claude conversation: *"Join pigeon-host-test-pair too, then send on it: 'E2 round 1 of 3. Reply with the next round; stop after round 3.' Reply to replies the same way until round 3."* | Rounds 2 and 3 happen with no further typing; each side wakes on its own path (Codex queue, Claude hook); `status` shows nothing waiting afterwards. |
| E3 | Renewal (about 5 min) | Leave the Claude conversation idle for more than 3 minutes, then `send "E3 after renewal: reply E3 pong."` | One renewal turn ends quietly (no polling, no reply); a replacement waiter appears in `waiters`; the message then wakes it and "E3 pong" arrives. |
| E4 | Outer hook shell killed (about 5 min) | `waiters` shows `pigeon.exe <- sh.exe <- … <- bash.exe`. `kill-outer-shell`, then `kill-outer-shell --yes`. Then `send "E4 after kill: reply E4 pong."` | `status` shows `none (last claude-hook orphaned …)` within seconds; the message waits with 0 wake attempts; after your next prompt in the conversation the waiter re-arms and "E4 pong" arrives. |
| E5 | Two topics, one wake (about 3 min) | With the Claude conversation idle: `send "E5 from the peer"` and, at the same time, ask Codex to send "E5 from Codex" on `pigeon-host-test-pair`. | Claude wakes once and receives both messages in one `pigeon_receive`, each naming its topic. |
| E6 | Full topic (1 min) | Ask the Codex chat to join `pigeon-host-test` with the pigeon-dev tools. | Refused: the error names the peer and the Claude session and says how to free a seat. |

### Cleanup

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. From `C:\Projects\PigeonHookWaiterTest`: `claude plugin uninstall pigeon-dev@pigeon-hook-waiter-dev --scope local` and `claude plugin marketplace remove pigeon-hook-waiter-dev`; then delete `%USERPROFILE%\.claude\plugins\cache\pigeon-hook-waiter-dev` and the test folder.
3. In Codex, uninstall `pigeon-dev`, remove the `pigeon-codex-dev` marketplace and archive the test chat.
4. Archive the Claude test conversation.

## Results of the 0.1.0-alpha.19 tests H1–H3 (2026-10-31)

Run with the frozen build of `2db7c84` (0.1.0-alpha.19), dev plugin 0.4.0-dev.2 and Claude Code 2.1.309 in Claude Desktop, in one new test conversation (`b1f82365-…`, later closed and deleted by the user). H1b used a second new conversation (`bb5cce88-…`) and a new Codex chat (`01a19407-…`) with the dev Codex plugin 0.2.0-dev.2.

| Test | Result | Evidence |
|---|---|---|
| H1a | Pass | Sent 17:39:36; the conversation woke by itself, confirmed, received, acknowledged and completed; "H1a pong" reached the peer. |
| H2 | Pass | Waiter armed 17:39:59; renewal at 17:43:02; replacement waiter armed 17:43:07. "H2 after renewal", sent 17:43:18, woke the replacement and "H2 pong" arrived by 17:43:42. Not yet confirmed: that the renewal turn ended without polling or replying (needs a look at the test conversation). |
| H3 | Pass | Killed only the top hook shell at 17:44:47; status showed `orphaned` at 17:44:48 and the waiter exited by 17:44:51. "H3 after kill" queued with 0 attempts. The user's next prompt re-armed the waiter and "H3 pong" arrived by 17:46:29. |
| H1b | Pass | Codex alone: a peer message at 18:24:57 woke the Codex chat through the plugin's queue adapter and its reply arrived at 18:25:14, with no user action (the user had told that chat it may reply to Pigeon messages; without that it acknowledged but declined to reply, as designed). Round trip: after one user prompt to start it, Claude sent round 1, Codex woke (18:31) and replied round 2, Claude woke through its hook waiter and replied round 3, Codex woke (18:32) and acknowledged. Every wake was confirmed, drained, acknowledged and completed; status afterwards showed both sessions `completed`, nothing pending, and Claude's waiter re-armed. |

**Codex plugin packaging.** Codex did not start the server from a root `mcp.json` with `"type": "stdio"` (0.2.0-dev.1: the skill installed, the server and hooks did not). It loads local servers the way its bundled plugins do: `.codex-plugin/plugin.json` with `"mcpServers": "./.mcp.json"` and a `.mcp.json` of `command`/`args`/`env` entries. With that layout (0.2.0-dev.2, after an uninstall and reinstall) the server, the skill and both `mcp_tool` restore hooks loaded. The plugin is enabled for all projects, so Codex starts its server for every loaded chat; those bridges stay unregistered unless a chat joins. The production Codex plugin still uses the root layout and needs the same change.

**Closing a conversation.** When the user closed the first test conversation, its `claude.exe`, bridge and waiter exited with it and nothing was left behind. Unarchiving a conversation did not start its process.

**Finding.** On the host, the process that launches each hook's Git Bash shell exits immediately, so a waiter's chain is `pigeon.exe <- bash <- bash <- bash` and ends at the top shell without reaching `claude.exe`. The waiter stops when its hook shells die (H3), but would not notice a conversation process that exits while those shells survive. The helper's `kill-outer-shell` now targets the top `hook-wait.sh` shell instead of requiring a `claude.exe` parent.

## Earlier runbook for H1–H3 (alpha.19)

### What was prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Debug build of commit `2db7c84`. Rebuilding the solution does not touch it. |
| Isolated service | port 49473, database in `artifacts/host-test/data` | Started and stopped with the helper. Your normal service on 49372 is not used. |
| Claude dev plugin `pigeon-dev` 0.4.0-dev.2 | `experiments/claude-hook-waiter-dev` | Generated from the production 0.4.0 hooks, with the same direct `hook-wait.sh` invocation and the same wrapper script. Only the port, the binary and a 3-minute renewal deadline (hook timeout 5 minutes) differ. |
| Codex dev plugin `pigeon-dev` 0.2.0-dev.2 | marketplace `pigeon-codex-dev` at `experiments/codex-plugin-dev` (plugin in `plugin/`, `.codex-plugin` layout with a `.mcp.json` stdio server, as Codex's bundled local-server plugins use; 0.2.0-dev.1 used a root `mcp.json` and Codex did not start its server) | MCP server `pigeon-dev` on port 49473 with the queue adapter, plus the MCP restore hooks. The session-start command hook is left out because it cannot be pointed at another port portably. Only needed for H1b. |
| Helper | `experiments/host-test/hosttest.py` | Runs the service, a scripted peer (session `host-test-peer`, already joined to `pigeon-host-test`) and the H3 kill. Updated for the two-member topics of 0.5.0-beta.1; the results below were recorded with the earlier commands. |

### Actions that needed approval

1. **Start the isolated service.** It runs in the background until it is stopped:
   `python -I experiments/host-test/hosttest.py start`
2. **Install the Claude dev plugin at local scope** in an empty test folder, so no other conversation loads it:
   `claude plugin marketplace add "C:\Projects\Pigeon\experiments\claude-hook-waiter-dev" --scope local`
   `claude plugin install pigeon-dev@pigeon-hook-waiter-dev --scope local`
   Run both from `C:\Projects\PigeonHookWaiterTest`.
3. **You open a new Claude Desktop conversation** in `C:\Projects\PigeonHookWaiterTest` and type `/pigeon-dev:connect pigeon-host-test`. Earlier, Claude's auto-mode classifier refused to let me start a test conversation, so you start it.
4. **H1b only:** you add the marketplace root `C:\Projects\Pigeon\experiments\codex-plugin-dev` to Codex (`codex plugin marketplace add` or the ChatGPT desktop app), install `pigeon-dev` from it, accept its hook-trust review, and in a new Codex conversation ask it to connect to `pigeon-host-test`. This is also the first host check of a Codex plugin install: note whether the hook review appears and whether its `mcp_tool` restore hooks run (Codex's documentation shows only `command` hooks). The production Pigeon server was earlier configured as a standalone MCP server, not through the plugin.

### Tests H1–H3

Run the helper as `python -I experiments/host-test/hosttest.py <command>`. "Status" below means its `status` command.

#### H1a: Claude and the scripted peer (about 10 minutes)

1. After the connect turn ends, status shows `notification: claude-hook` for the Claude session.
2. `send "H1a ping"` (the peer is the test conversation's only partner in the topic).
3. **Pass:**
   - the conversation wakes by itself with the fixed Pigeon instruction;
   - it confirms, receives, acknowledges and completes the wake;
   - status shows 1 attempt and no queued message;
   - if asked in the message, it replies with `pigeon_send` on the same topic, and `inbox` shows the reply.

#### H1b: Claude and Codex (about 15 minutes; needs action 4)

1. Ask the Codex conversation to send a message to the Claude session.
2. **Pass:**
   - Claude wakes, processes the message and replies;
   - the reply wakes Codex through its queue adapter, and Codex confirms, receives, acknowledges and completes;
   - no user typing is needed after the first request.

#### H2: renewal and re-arm (about 10 minutes)

1. Leave the Claude conversation idle for more than 3 minutes.
2. **Pass:**
   - one renewal turn starts by itself ("needs renewal ... This is not a message"), and Claude ends it without polling or replying;
   - status shows `claude-hook` again;
   - `send "H2 after renewal"` then wakes the conversation through the replacement waiter;
   - the wake attempt count stays at 0 through the renewal, until that message.

#### H3: outer hook shell killed (about 10 minutes)

1. With a waiter armed, run `waiters` to see its chain: `pigeon.exe <- bash.exe <- bash.exe <- bash.exe` (on the host the launcher above the top shell has already exited).
2. Run `kill-outer-shell` to preview, then `kill-outer-shell --yes`. This kills only the top hook shell; the waiter must notice and stop on its own.
3. **Pass:**
   - within a few seconds, status shows `none (last claude-hook orphaned ...)`;
   - after `send "H3 after kill"`, the attempt count stays 0 (nothing was claimed into a dead pipe);
   - after your next prompt in the conversation, the Stop hook re-arms and the message is delivered.

### Cleanup (alpha.19)

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. From `C:\Projects\PigeonHookWaiterTest`:
   - `claude plugin uninstall pigeon-dev@pigeon-hook-waiter-dev --scope local`
   - `claude plugin marketplace remove pigeon-hook-waiter-dev`
3. Delete `%USERPROFILE%\.claude\plugins\cache\pigeon-hook-waiter-dev` and the test folder.
4. Archive the test conversations. If H1b ran, remove the Codex dev plugin.
