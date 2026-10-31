# Claude hook waiter: plan

Status, 2026-10-31: milestones 1–3 done (see "Milestone 3 results" at the end); milestones 4 and 5 not started. The production plugin is unchanged. Evidence: [the probe report](claude-resume-probe-report.md) (probe runs 1–6), Codex's review of it, and the end-to-end results below.

## Goal

Give a Claude Desktop conversation a Pigeon wake path that does not depend on the Monitor tool or a user prompt, using a `Stop` command hook with `async: true` and `asyncRewake: true` that waits for one wake and exits 2. Keep the existing wake protocol: the agent still confirms the generation, receives, acknowledges and completes through MCP.

Non-goals for this iteration: replacing the Monitor or channel adapters, the terminal plugin-monitor adapter (a separate path), periodic model turns, and any change to message leasing.

## 1. CLI: `pigeon hook wait claude-code`

A new hook subcommand beside `pigeon hook session-start claude-code`, implemented as a bounded, one-wake variant of `ClaudeMonitor`. It is invoked only by hooks.

1. **Input**: read stdin with the same 8 KiB bound as `SessionHook`, take only `session_id`. Never read transcripts or prompts.
2. **Association check**: if the `(claude-code, session_id)` pair has no saved association, exit 0 immediately with no output. This keeps the hook cheap in unconnected conversations.
3. **Bridge match**: as in `ClaudeMonitor`, require exactly one active `mcp` bridge whose binding is `claude-code` + this session ID. Keep polling while there is none (the bridge may still be starting) until the deadline.
4. **Ownership** (section 2): acquire the session's notifier ownership with mode `claude-hook` and a random holder ID; renew it each tick; release it on every exit path.
5. **Loop** (1 s tick, as today): bridge heartbeat with mode `claude-hook`, ownership renewal, then `wake_claim` with mode `claude-hook`, the session ID and the holder ID.
6. **Wake**: on a claim, write `WakePrompt.For(generation)` to **stderr** as one line, record `wake_result` `transport-written`, release ownership, exit **2**.
7. **Deadline**: `--timeout <seconds>` (default chosen from the hook-timeout tests, section 5). Exit 0 a margin before the hook's own timeout so the host never kills it mid-claim. Record the expiry so `pigeon status` can show it (section 2).
8. **Superseded or lost ownership**: exit 0 with no output.
9. **Service unavailable or restarted**: keep retrying silently until the deadline; never emit a false wake.
10. **Orphan safety**: reuse `MonitorParent`; exit 0 without claiming if the launching process ends. To verify: under a hook the parent is the hook shell, not Claude Code.

**Exit-code rule**: in hook mode, exit 2 means "wake Claude" and the stderr text is shown to the model. So every non-wake outcome, including argument errors, bad input and exceptions, must exit 0 or 1, never 2, and stderr must contain only the fixed `WakePrompt` line or nothing. This differs from the CLI's usual "invalid arguments exit 2" convention; the hook command needs its own error mapping and an outer catch-all.

## 2. Service: one notifier per session

Today `wake_claim` is per agent and transactional, so two claimants never get the same generation inside a retry window, but whichever adapter claims first delivers. A hook waiter, a Monitor and a channel on the same session would race. Codex's point 3 asks for explicit ownership.

- **New in-memory registry** in `Broker`, keyed by `(project, agent)`: `{holderId, mode, expiresAt}`. Lost on service restart; holders re-acquire on their next tick (they already retry).
- **Operations**: `wake_owner_acquire {mode, holderId, sessionId}`, `wake_owner_release {holderId, reason}`. Renewal piggybacks on `bridge_heartbeat` (add `holderId`). Ownership expires 15 s after the last renewal, matching the bridge registry.
- **Policy**:
  - Same mode, new holder → the newer holder supersedes the older (each turn's `Stop` hook replaces the previous waiter).
  - Different mode, live holder → the request is refused. The newcomer exits 0 (hook) or keeps waiting without claiming (Monitor), so a running Monitor or channel is never displaced by a hook.
  - An expired holder can be replaced by any mode.
- **`ClaimWake`**: accept mode `claude-hook` (runtime `claude-code`, session ID must match the binding, like `claude-monitor`). For `claude-hook`, `claude-monitor` and `claude-channel`, require the caller to be the current owner.
- **Existing adapters**: `ClaudeMonitor` and `ClaudeChannel` acquire ownership too, so all three follow one rule. `codex-queue` is unchanged.
- **Bridge modes**: add `claude-hook` to `BridgeHeartbeat`'s allowed modes.
- **Visibility** (Codex point 4): keep the last release reason and time per identity (`expired`, `superseded`, `woke`, `orphaned`). `pigeon status` shows, for a Claude MCP bridge, either the active owner's mode or `notification: none (hook waiter expired at …)`, replacing the current "no active monitor" check.

## 3. Plugin hooks (development copy first)

Add to `integrations/claude-plugin/hooks/hooks.json`, keeping the existing `SessionStart` context hook and the `UserPromptSubmit` MCP restore hook:

```json
"Stop": [{
  "hooks": [{ "type": "command", "command": "pigeon hook wait claude-code",
              "async": true, "asyncRewake": true, "timeout": <from tests> }]
}]
```

- `async: true` is required: in Desktop 2.1.309, `asyncRewake` alone ran synchronously and never woke the model.
- Optional second entry under `SessionStart` with the same command and flags, to cover a brand-new conversation before its first turn and any reopen that does start the process before a prompt. The same-mode supersede rule keeps one waiter.
- **Connect skill and SessionStart context**: when a `claude-hook` owner is active, do not start a Monitor. Otherwise keep today's instruction. Decide after the competition test.
- The wake appears in the transcript as "Stop hook feedback"; document it.

## 4. Automated tests (no real host)

Following the existing Gherkin and unit-test structure, with disposable services and databases:

- **Mailbox and broker**:
  - `claude-hook` claim validation (runtime, session match).
  - Ownership: acquire, same-mode supersede, cross-mode refusal, expiry takeover, release reasons, loss on restart.
  - `ClaimWake` rejects a non-owner.
  - Existing monitor and channel scenarios still pass with ownership.
- **Process-level `pigeon hook wait`**:
  - No association → exit 0, no output, fast.
  - Publish → exactly one stderr line equal to `WakePrompt.For(g)`, exit 2, `transport-written`, no message leased.
  - Burst → one wake.
  - Superseded by a second waiter → first exits 0.
  - Cross-mode owner present → exit 0.
  - Deadline → exit 0, and status shows `expired`.
  - Service down for the whole run → exit 0, no wake.
  - Service restart mid-wait → later wake delivered.
  - Malformed, oversized or missing `session_id` → exit 0 or 1, never 2.
  - Parent exit → no claim.
  - No peer data in any output.

## 5. Isolated end-to-end tests on real hosts

Setup (needs your approval before running):
- A disposable service on a non-default port with a temporary database.
- A copy of the Claude plugin whose `.mcp.json` and hook commands set that port.
- Installed at local scope, with the config change shown first. A synthetic topic only, and synthetic messages sent from the CLI.
- No Project Sigma, no production database.

| # | Scenario | Pass criteria |
|---|---|---|
| 1 | Idle wake: join, idle ≥ 3 min, send one message | Wake turn without a prompt; confirm, receive, ack, complete; DB shows acknowledged and wake completed |
| 2 | Re-arm: a second message after the first wake | Second wake from the waiter started by the wake turn's `Stop` |
| 3 | Message arrives while the agent is busy | No loss; wake after the turn ends; one generation |
| 4 | Burst of 5 messages | One wake generation; all 5 acknowledged |
| 5 | Service restart while waiting | Wake still delivered after restart; no duplicate |
| 6 | Idle past the waiter deadline | Status shows `expired`; the message waits until the next turn; the delay is recorded |
| 7 | Hook timeout limits | Highest `timeout` Desktop accepts (try 1800, 3600, 7200 s) and what happens at the limit |
| 8 | Close/reopen without a message (archive → unarchive → open; app restart) | Recorded either way: process start, `SessionStart` waiter, wake; then one message re-arms |
| 9 | Monitor tool and hook waiter on the same session | Exactly one owner; no double wake |
| 10 | Several conversations in the project, one connected | Only the connected one holds a waiter; others exit at once |
| 11 | Plugin disabled or uninstalled while loaded | Waiter behaviour recorded; no wakes for unconnected sessions |
| 12 | Desktop app restart | Recorded; recovery path after the first message |

The same subset (1, 2, 5, 8 via `--resume`) also runs in the interactive terminal CLI for comparison.

Global pass criteria:
- Every message acknowledged exactly once.
- No wake for another session.
- No peer data in wake text.
- No orphan processes after each scenario.
- `pigeon status` accurate throughout.

## 6. Milestones

1. **Service and CLI with automated tests**: ownership, `claude-hook` mode, `pigeon hook wait`, status visibility. No host involved.
2. **Development plugin copy** with the `Stop` (and optional `SessionStart`) hook.
3. **Isolated E2E in Desktop and CLI** (approval required).
4. **Docs**: README Claude Desktop section, `docs/session-recovery.md`, `docs/pigeon-wait.md` note.
5. **Production plugin version bump**, only after milestone 3 passes. Until then the approach stays "promising, not reliable recovery".

## 7. Risks and open questions

- `asyncRewake` is documented for reacting to background failures, not as an event loop. Desktop behaviour may change with a Desktop update; the E2E tests should be re-run per Desktop Claude Code version.
- Hook timeout ceiling, and whether reaching it kills the process without a wake (assumed).
- Desktop reopen is inconsistent: one observed reopen started the process before a prompt, two did not. Neither guarantees recovery.
- Every connected wake costs a model turn; every turn end in any conversation of the project spawns a short-lived `pigeon` process. Measure the unconnected fast-exit cost.
- Windows process tree under hooks (bash shell parent) for orphan detection.
- Directory-marketplace plugins load hooks from source; already-loaded sessions keep running old hook definitions after an update or uninstall.
- Whether in-memory ownership is enough, or ownership should survive a service restart.

## Milestone 3 results (2026-10-31)

Setup: Claude Desktop with its bundled Claude Code 2.1.309 on Windows 11. The development plugin (`experiments/claude-hook-waiter-dev`) was installed at local scope in a separate folder, `C:\Projects\PigeonHookWaiterTest`, so the production plugin was not loaded. An isolated service ran the Debug build on port 49473 with a temporary database, and a synthetic topic `hook-e2e` had a CLI sender. Test conversations ran in auto permission mode. 21 synthetic messages were sent; every one was acknowledged exactly once, and none was lost or duplicated. Afterwards the plugin, marketplace, cache, service, database and test processes were removed, and user configuration was verified against backups.

| # | Scenario | Observed |
|---|---|---|
| 1 | Idle wake: join, idle 3 min 12 s, one message | Pass. Wake about 1 s after sending ("Stop hook feedback"); confirm, receive, acknowledge, complete; waiter re-armed |
| 2 | Re-arm | Pass. A second message to the re-armed waiter woke the session again |
| 3 | Message while the session is busy | Pass. The waiter claimed during a long writing turn; Desktop held the wake until the turn ended (25 s later) and then delivered it. A foreground sleep is blocked by the host, and a ping disguised as one was refused by the test session, so a real busy turn was used |
| 4 | Burst of 5 | Pass. One wake generation; all 5 acknowledged |
| 5 | Service restart while waiting | Pass. The same waiter process reacquired ownership after the restart and woke the session for a message sent afterwards |
| 6 | Idle past the waiter deadline (570 s) | As designed. Clean exit at 570 s; status showed `notification: none (last claude-hook expired at …)`; a message then stayed queued until the next turn (78 s here, set by when a turn was prompted) |
| 7a | Hook timeout 60 s, waiter deadline 120 s | Desktop killed the waiter and its shell at 60 s with no wake and no transcript entry; Pigeon recorded `lost` about 15 s later. Keep the waiter deadline below the hook timeout |
| 7b | Hook timeout 900 s | Honoured: the waiter was alive at 636 s and still woke the session. No upper limit was searched for |
| 8 | Close (archive) and reopen without a message | Pass this time: the process started on reopen and the `SessionStart:resume` waiter delivered a message queued while it was closed. Two earlier probe reopens did not start the process, so reopen recovery is not reliable |
| 9 | A Monitor and the hook waiter on one session | No double wake. The hook waiter delivered while it owned the session; when it released after waking, the Monitor took over and the next hook waiter backed off; the Monitor delivered the next message. **Gap:** a Monitor stopped with TaskStop is killed without releasing, the next hook waiter was refused and exited, and the session had no waiter until its next turn. Fixed afterwards (below) |
| 10 | Several conversations | Pass. An unconnected conversation kept no waiter; a message to one connected conversation woke only that one |
| 11 | Plugin uninstalled while loaded | The waiter already running still delivered once. The conversation's next Stop hook ran without the Pigeon waiter (Desktop dropped the uninstalled plugin's hooks); its dev MCP server process kept running until stopped |
| 12 | Desktop app restart, message sent while closed | Only the conversation whose pop-out window Desktop restored came back by itself; its `SessionStart:resume` waiter delivered the message. Conversations without an open window had no process or waiter until opened |

Other observations:

- Wakes appear in the transcript as "Stop hook feedback" / "Stop hook blocking error from command …".
- Editing `hooks.json` does not change hooks in conversations that already loaded the plugin; uninstalling does remove them.
- The hook commands run under Git Bash (`VAR=value cmd` form).

### Fix after milestone 3

`pigeon hook wait claude-code` no longer exits when a Monitor or channel owns the session. It waits without claiming and takes over when that owner releases or its ownership lapses (15 s after its last renewal), so a killed Monitor no longer leaves the session without a waiter. It still exits at once when a newer hook waiter replaces it. Covered by the `ClaudeHookWait` integration scenario, and re-tested in Desktop 2.1.309 the same day: a Monitor started first owned the session, and the conversation's Stop waiter stayed alive behind it. After the Monitor process was killed (as TaskStop or a crash would), the same waiter took ownership about 19 s later without any new turn, and a message sent then was delivered through it and acknowledged once.

### Assessment

While a conversation's process is running, the Stop-hook waiter delivered every message without a prompt, including after a service restart and alongside a Monitor. It is not full recovery: after the waiter's deadline a message waits for the next turn, and after a reopen or app restart delivery depends on Desktop starting the conversation's process, which it did only when the conversation was shown.
