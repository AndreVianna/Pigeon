# Host tests H1–H3: runbook

Live tests from [the release plan](../../docs/release-plan.md), Phase 3. They check this branch's build in the real Claude Desktop (and, for H1b, Codex) against an isolated service.

## Results (2026-10-31)

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

## What is prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Debug build of commit `2db7c84`. Rebuilding the solution does not touch it. |
| Isolated service | port 49473, database in `artifacts/host-test/data` | Started and stopped with the helper. Your normal service on 49372 is not used. |
| Claude dev plugin `pigeon-dev` 0.4.0-dev.2 | `experiments/claude-hook-waiter-dev` | Generated from the production 0.4.0 hooks, with the same direct `hook-wait.sh` invocation and the same wrapper script. Only the port, the binary and a 3-minute renewal deadline (hook timeout 5 minutes) differ. |
| Codex dev plugin `pigeon-dev` 0.2.0-dev.2 | marketplace `pigeon-codex-dev` at `experiments/codex-plugin-dev` (plugin in `plugin/`, `.codex-plugin` layout with a `.mcp.json` stdio server, as Codex's bundled local-server plugins use; 0.2.0-dev.1 used a root `mcp.json` and Codex did not start its server) | MCP server `pigeon-dev` on port 49473 with the queue adapter, plus the MCP restore hooks. The session-start command hook is left out because it cannot be pointed at another port portably. Only needed for H1b. |
| Helper | `experiments/host-test/hosttest.py` | Runs the service, a scripted peer (`mock`/`host-test-peer`) and the H3 kill. Dry-run checked against a throwaway service. |

## Actions that need your approval

1. **Start the isolated service.** It runs in the background until it is stopped:
   `python -I experiments/host-test/hosttest.py start`
2. **Install the Claude dev plugin at local scope** in an empty test folder, so no other conversation loads it:
   `claude plugin marketplace add "C:\Projects\Pigeon\experiments\claude-hook-waiter-dev" --scope local`
   `claude plugin install pigeon-dev@pigeon-hook-waiter-dev --scope local`
   Run both from `C:\Projects\PigeonHookWaiterTest`.
3. **You open a new Claude Desktop conversation** in `C:\Projects\PigeonHookWaiterTest` and type `/pigeon-dev:connect pigeon-host-test`. Earlier, Claude's auto-mode classifier refused to let me start a test conversation, so you start it.
4. **H1b only:** you add the marketplace root `C:\Projects\Pigeon\experiments\codex-plugin-dev` to Codex (`codex plugin marketplace add` or the ChatGPT desktop app), install `pigeon-dev` from it, accept its hook-trust review, and in a new Codex conversation ask it to connect to `pigeon-host-test`. This is also the first host check of a Codex plugin install: note whether the hook review appears and whether its `mcp_tool` restore hooks run (Codex's documentation shows only `command` hooks). The production Pigeon server was earlier configured as a standalone MCP server, not through the plugin.

## Tests

Run the helper as `python -I experiments/host-test/hosttest.py <command>`. "Status" below means its `status` command.

### H1a: Claude and the scripted peer (about 10 minutes)

1. After the connect turn ends, status shows `notification: claude-hook` for the Claude session.
2. `send claude-code <claude-session-id> "H1a ping"`.
3. **Pass:**
   - the conversation wakes by itself with the fixed Pigeon instruction;
   - it confirms, receives, acknowledges and completes the wake;
   - status shows 1 attempt and no queued message;
   - if asked in the message, it replies with `pigeon_send_session` to `mock`/`host-test-peer`, and `inbox` shows the reply.

### H1b: Claude and Codex (about 15 minutes; needs action 4)

1. Ask the Codex conversation to send a message to the Claude session.
2. **Pass:**
   - Claude wakes, processes the message and replies;
   - the reply wakes Codex through its queue adapter, and Codex confirms, receives, acknowledges and completes;
   - no user typing is needed after the first request.

### H2: renewal and re-arm (about 10 minutes)

1. Leave the Claude conversation idle for more than 3 minutes.
2. **Pass:**
   - one renewal turn starts by itself ("needs renewal ... This is not a message"), and Claude ends it without polling or replying;
   - status shows `claude-hook` again;
   - `send ... "H2 after renewal"` then wakes the conversation through the replacement waiter;
   - the wake attempt count stays at 0 through the renewal, until that message.

### H3: outer hook shell killed (about 10 minutes)

1. With a waiter armed, run `waiters` to see its chain: `pigeon.exe <- bash.exe <- bash.exe <- bash.exe` (on the host the launcher above the top shell has already exited).
2. Run `kill-outer-shell` to preview, then `kill-outer-shell --yes`. This kills only the top hook shell; the waiter must notice and stop on its own.
3. **Pass:**
   - within a few seconds, status shows `none (last claude-hook orphaned ...)`;
   - after `send ... "H3 after kill"`, the attempt count stays 0 (nothing was claimed into a dead pipe);
   - after your next prompt in the conversation, the Stop hook re-arms and the message is delivered.

## Cleanup

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. From `C:\Projects\PigeonHookWaiterTest`:
   - `claude plugin uninstall pigeon-dev@pigeon-hook-waiter-dev --scope local`
   - `claude plugin marketplace remove pigeon-hook-waiter-dev`
3. Delete `%USERPROFILE%\.claude\plugins\cache\pigeon-hook-waiter-dev` and the test folder.
4. Archive the test conversations. If H1b ran, remove the Codex dev plugin.
