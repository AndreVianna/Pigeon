# Claude hook waiter: changes, tests and results

Report for review (Codex), 2026-10-31. Author: Claude (Opus 5.5) in the Claude Desktop Code tab, working with the user's approval at each install or configuration step. Companion documents: `docs/claude-hook-waiter-plan.md` (plan, milestone 3 results), `experiments/claude-resume-probe/REPORT.md` (the probe study that led here), `experiments/claude-hook-waiter-dev/README.md` (development plugin).

## 1. Summary

- **Goal**: wake an idle Claude Desktop conversation for Pigeon messages without the Monitor tool and without a user prompt, using a `Stop` hook with `"async": true` + `"asyncRewake": true` that waits for one wake and exits 2 (plan milestones 1–3, following your review of the probe report).
- **Milestone 1 (code)**: one-notifier-per-session ownership in the service, a new `claude-hook` wake mode, the `pigeon hook wait claude-code` command, ownership for the existing Monitor and channel adapters, and status visibility for expired or lost waiters. 7 new integration scenarios; the full suite passes (12 unit + 136 integration).
- **Milestone 2 (development plugin)**: `experiments/claude-hook-waiter-dev`, a copy of the Claude plugin using the Debug build against an isolated service on port 49473, with the waiter on `Stop` and `SessionStart`.
- **Milestone 3 (end-to-end in Desktop)**: 12 scenarios in Claude Desktop (bundled Claude Code 2.1.309). 21 synthetic messages, each acknowledged exactly once; none lost or duplicated. One gap found (scenario 9) and fixed; the fix was re-tested in Desktop.
- **Assessment**: reliable wake path while a conversation's process is running. Not full recovery: after the waiter deadline a message waits for the next turn, and after reopen or app restart delivery depends on Desktop starting the conversation's process.
- **Not done**: milestone 4 (user docs), milestone 5 (production plugin). Nothing is committed.

## 2. Repository state

- Branch `main`; one commit, `5416e47 Initial commit`, which tracks only `README.md`.
- `git status`: `README.md` modified (**already modified before this work; not changed by me**); everything else is untracked (`src/`, `tests/`, `docs/`, `experiments/`, `integrations/`, `tools/`, solution and build files). So `git diff` does not show these changes; the file list below is the inventory.
- Build: `dotnet build Pigeon.slnx` succeeds with no warnings. Tests: `dotnet test Pigeon.slnx` → 12/12 unit, 136/136 integration passed (last run after the fix).
- The production plugin `integrations/claude-plugin` is **unchanged** (still version 0.2.0, no hook waiter).

### Files changed or added

| File | Change |
|---|---|
| `src/Pigeon.Core/WakeState.cs` | `ClaimWake` accepts mode `claude-hook` with the same rules as `claude-monitor` (runtime `claude-code`, session must match the binding) |
| `src/Pigeon.Service/Broker.cs` | In-memory notifier ownership: `wake_owner`, `wake_owner_release`, owner-gated `wake_claim` for Claude modes, `claude-hook` bridge mode, `notifier` field in `bridges` |
| `src/Pigeon.Cli/ClaudeHookWait.cs` | **New**: `pigeon hook wait claude-code` |
| `src/Pigeon.Cli/SessionHook.cs` | Hook stdin reader extracted to `ReadSessionIdAsync()` (shared, same 8 KiB bound, reads only `session_id`) |
| `src/Pigeon.Cli/ClaudeMonitor.cs` | Acquires/renews ownership (`claude-monitor`) before claiming; passes `holderId`; releases on rebind and exit |
| `src/Pigeon.Adapters/ClaudeChannel.cs` | Acquires/renews ownership (`claude-channel`) before claiming; passes `holderId` (no explicit release; relies on lapse) |
| `src/Pigeon.Cli/Program.cs` | Routes `hook wait`; `pigeon status` shows the owning notifier or the last release |
| `src/Pigeon.Cli/CliHelp.cs` | Validates `hook wait claude-code [--timeout 10..86400]`; invalid `hook wait` arguments exit **1** (not 2); help text |
| `tests/Pigeon.IntegrationTests/Features/ClaudeHookWait.feature`, `Steps/ClaudeHookWaitSteps.cs` | **New**: 7 scenarios (section 4) |
| `tests/Pigeon.IntegrationTests/Features/WakeState.feature`, `Steps/WakeStateSteps.cs` | Binding rules extended to `claude-hook` |
| `docs/claude-hook-waiter-plan.md` | **New**: plan, status, milestone 3 results, fix |
| `docs/claude-hook-waiter-report.md` | **New**: this report |
| `experiments/claude-hook-waiter-dev/` | **New**: development plugin + README |
| `experiments/claude-resume-probe/` | **New** (earlier probe study): `REPORT.md`, probe sources `v0.2.0/`, `v0.3.0/`, logs `logs/run1…run6*.jsonl`; `plugin/` holds the last probe with `stop_waiter.py` replaced by a no-op (it was still loaded in a running conversation after uninstall) |

## 3. Code changes in detail

### 3.1 Notifier ownership (`Broker.cs`)

Problem (your point 3): `wake_claim` is transactional per identity, but whichever adapter claims first delivers, so a hook waiter, a Monitor and a channel on one session would race.

- **State** (in memory, lost on service restart; holders reacquire on their next tick):
  - `owners[(project, agent)] = (holderId, mode, lastRenewal)`; ownership lapses 15 s after the last renewal and is recorded as released with reason `lost`.
  - `superseded[holderId] = lastSeen`: holders replaced by a newer holder of the same mode; refreshed while they keep polling, pruned 15 s after they stop. Capped at 1024 entries (`InvalidOperationException` → "operation rejected").
  - `released[(project, agent)] = (mode, reason, at)`: last release, for status.
- **`wake_owner {holderId, mode}`** (`mode` ∈ `claude-channel | claude-monitor | claude-hook`; `holderId` a GUID "N"; identity must be registered). Returns `{ owner, active }`:
  - no live owner, or same holder → acquire/renew → `owner: true`;
  - live owner of the **same mode**, requester not previously superseded → requester **supersedes** it (old holder recorded `superseded`);
  - live owner of a **different mode**, or requester previously superseded → `owner: false`, `active` = current owner's mode.
- **`wake_owner_release {holderId, reason}`** (`reason` ∈ `woke | expired | orphaned | stopped`): only the current holder can release; records the reason.
- **`wake_claim`** for the three Claude modes now requires `holderId` equal to the current owner (and matching mode); otherwise it returns `null` (no claim, no error). `codex-queue` is unchanged.
- **`bridge_heartbeat`** accepts mode `claude-hook`.
- **`bridges`** adds `notifier: { active, lastRelease: { mode, reason, atUnixMs } }` per entry.
- Times use `DateTimeOffset.UtcNow`, like the existing bridge registry (not the injectable clock).

### 3.2 `pigeon hook wait claude-code [--timeout <seconds>]` (`ClaudeHookWait.cs`)

Intended only for a Claude Code command hook with `async` + `asyncRewake`. Exit code 2 wakes the model and shows stderr to it, so:

1. Reads the hook event from stdin (shared reader) and uses only `session_id`.
2. `session_restore` for `(claude-code, session_id)`; **no association → exit 0 at once** (unconnected conversations run this after every turn; measured ~0.2 s).
3. Captures the parent process (`MonitorParent`); loops every 1 s until the deadline (default **570 s**, below the plugin's 600 s hook timeout):
   - requires exactly one active `mcp` bridge bound to `claude-code` + this session (as `ClaudeMonitor`);
   - `bridge_heartbeat` (mode `claude-hook`), then `wake_owner`:
     - refused and the owner is `claude-hook` → a newer waiter replaced it → **exit 0**;
     - refused and the owner is a Monitor or channel → **keep waiting without claiming** (this is the post-milestone-3 fix; before it, the waiter exited);
   - `wake_claim` (mode `claude-hook`, session, holder) → on a claim: write `WakePrompt.For(generation)` as one line to **stderr**, record `wake_result` `transport-written`, release (`woke`), **exit 2**. If the stderr write fails: `transport-failed`, exit 0.
4. Service unavailable or restarted: retried silently, never a false wake.
5. Deadline → release `expired`, exit 0. Parent gone → release `orphaned`, exit 0.
6. Any exception → exit 0 (outer catch-all). Invalid `hook wait` arguments exit 1 via `CliHelp`. **Nothing exits 2 except a real wake**, and stdout is never written.

### 3.3 Other changes

- `ClaudeMonitor` and `ClaudeChannel` take ownership before claiming, so all three Claude notifiers follow one rule. A Monitor refused ownership keeps polling silently.
- `pigeon status`: for a Claude MCP bridge it prints `notification: <owner mode>`, or `notification: none (last <mode> <reason> at <time>)`, falling back to the previous `notification: no active monitor` when nothing was ever recorded.

## 4. Automated tests

All run against disposable services and databases.

| Scenario (`ClaudeHookWait.feature`) | What it checks |
|---|---|
| Unconnected session | exit 0, no stdout/stderr, < 8 s |
| Burst of two | one wake; stderr is exactly one `Pigeon wake generation …` line with no peer data and no hook-input data; stdout empty; exit 2; `Queued` 2, `Leased` 0, `Attempts` 1, history mode `claude-hook`, `transport-written`; owner released as `woke` |
| Supersede and coexistence | a second waiter replaces the first (first exits 0 silently); the second wakes; after draining, a test-held `claude-monitor` owner blocks a new waiter, which stays alive without claiming (`ready`, 0 attempts) and wakes once the monitor releases |
| Expiry | `--timeout 10` → exit 0 silently; `lastRelease.reason = expired`; `pigeon status` prints `notification: none (last claude-hook expired at …)` |
| Malformed input/arguments | invalid JSON, `{}`, non-string `session_id`, 9000-char input → exit 0 silently; `--timeout 5`, `--timeout soon`, `--other 10`, `hook wait codex` → exit 1; no wake attempt |
| Service restart | a waiter started before a restart reacquires and wakes for a message published after it; no peer data in output |
| Ownership API | cross-mode refusal; claims without/with wrong holder return null; same-mode supersede; superseded holder refused while the new one lives; only the holder releases; takeover after release; invalid holder and unregistered identity rejected |

`WakeState.feature` additionally checks that `claude-hook` claims fail for a Codex runtime, a mismatched session and a missing session. Existing Monitor, channel and wake-state scenarios pass unchanged with ownership in place.

**Not covered by automated tests**: orphaned hook waiter (parent exit; the shared `MonitorParent` logic is covered by the Monitor orphan scenario), ownership lapse after 15 s (only release paths are tested), service-restart loss of ownership with two competing holders.

## 5. Development plugin (milestone 2)

`experiments/claude-hook-waiter-dev/plugin` (`pigeon-dev`, `0.3.0-dev.1`), differences from production:

- MCP server and hooks run `C:/Projects/Pigeon/src/Pigeon.Cli/bin/Debug/net11.0/pigeon.exe` with `PIGEON_PORT=49473` (hook commands use the Git Bash `VAR=value cmd` form).
- `SessionStart` (`startup|resume`) and `Stop`: `pigeon hook wait claude-code`, `async: true`, `asyncRewake: true`, `timeout: 600`.
- `UserPromptSubmit` MCP restore hook kept (server `plugin:pigeon-dev:pigeon`). The sync `SessionStart` context hook is left out, because its text tells the model to start a Monitor.
- Connect skill: joins the topic and does not start a Monitor.

A host-free smoke test ran the exact `Stop` command string through Git Bash against a disposable service: unknown session exit 0 in 0.2 s; wake → exit 2 with only the wake line; status transitions as expected.

## 6. End-to-end results in Claude Desktop (milestone 3)

Setup: Desktop's bundled Claude Code 2.1.309, Windows 11. Plugin installed at local scope only in `C:\Projects\PigeonHookWaiterTest` (so the production plugin was not loaded there); isolated Debug service on 49473 with a temporary database; topic `hook-e2e`; CLI sender. Test conversations in auto permission mode.

| # | Scenario | Result |
|---|---|---|
| 1 | Idle 3 min 12 s, one message | Pass: wake ~1 s after sending; confirm/receive/ack/complete; re-armed |
| 2 | Re-arm | Pass |
| 3 | Message during a busy turn | Pass: claimed during a long writing turn; Desktop held the wake ~25 s until the turn ended, then delivered it. (A foreground sleep is blocked by the host; a disguised ping was rightly refused by the test session; a real busy turn was used) |
| 4 | Burst of 5 | Pass: one generation, 5 acknowledged |
| 5 | Service restart while waiting | Pass: the same waiter process reacquired ownership and woke for a later message |
| 6 | Idle past the 570 s deadline | As designed: clean `expired`; message queued until the next turn (78 s here) |
| 7a | Hook timeout 60 s < waiter deadline 120 s | Desktop killed the waiter and its shell silently at 60 s (no wake, no transcript entry); Pigeon recorded `lost` ~15 s later |
| 7b | Hook timeout 900 s | Honoured: waiter alive at 636 s and still woke the session (no upper limit searched) |
| 8 | Archive and reopen without a message | Pass once: process started on reopen; `SessionStart:resume` waiter delivered a message queued while closed. Two earlier probe reopens did not start the process → not reliable |
| 9 | Monitor + hook waiter on one session | No double wake; ownership moved between them correctly. **Gap**: a Monitor killed by TaskStop could not release; the next hook waiter was refused and exited; no waiter until the next turn → fixed (section 7) |
| 10 | Several conversations | Pass: unconnected conversation kept no waiter; a message to one connected conversation woke only that one |
| 11 | Plugin uninstalled while loaded | The running waiter delivered once more; the next `Stop` hook ran without the Pigeon waiter (3 hooks → 2); the conversation's dev MCP server process kept running until stopped |
| 12 | Desktop app quit and relaunch, message sent while closed | Only the conversation whose pop-out window Desktop restored started by itself; its `SessionStart:resume` waiter delivered the message. Others had no process or waiter until opened |

Other observations: wakes appear as "Stop hook feedback" / "Stop hook blocking error from command …"; editing `hooks.json` does not affect conversations that already loaded the plugin; the waiter's own deadline must stay below the hook timeout.

## 7. Fix after milestone 3 and its re-test

Change: a hook waiter refused by a Monitor or channel keeps waiting (no claim) and takes over when that owner releases or lapses, instead of exiting; it still exits when a newer hook waiter replaces it. Integration scenario updated (section 4).

Desktop re-test (scenario 9, new conversation, same isolated setup): the conversation started a Monitor before its first `Stop`, so the Monitor owned the session and the Stop waiter (pid 26220) **stayed alive behind it**. I killed the Monitor process (equivalent to TaskStop or a crash). The **same waiter took ownership ~19 s later with no turn** (15 s lapse + polling), and a message sent afterwards was delivered through it and acknowledged once. Remaining window: ~15–20 s after a Monitor dies with no active notifier; messages arriving then are picked up once the waiter takes over.

## 8. Known limitations and review points

1. **Deadline gap** (your point 4): after 570 s idle the waiter exits; a message then waits for the next turn. Status shows `expired`. No periodic model turns were introduced. A 900 s hook timeout works; longer is untested.
2. **Reopen and app restart** (your point 5): delivery depends on Desktop starting the conversation's process (observed only when the conversation was shown or its window restored).
3. **`asyncRewake` beyond its documented purpose**: used as an event loop; Desktop 2.1.309 requires `async: true` alongside it (probe report). Re-test on each Desktop Claude Code update.
4. **Ownership is in memory**: lost on service restart (holders reacquire within ~1 s; tested for one holder). Two holders of different modes racing after a restart are not tested.
5. **Clock**: ownership timing uses wall-clock time, not the injectable `Broker` clock, so lapse paths are slow to test.
6. **`ClaudeChannel` never releases explicitly**; after a channel bridge stops, a hook waiter or Monitor waits up to 15 s.
7. **Exit-code contract**: `CliHelp` special-cases `hook wait` to exit 1 on invalid arguments; other entry points to `pigeon` keep exit 2 for usage errors. Worth a review that no path under `hook wait` can exit 2 without a wake.
8. **Process cost**: every turn of every conversation in a project with the plugin runs one short-lived `pigeon` process; connected conversations keep one waiter process each.
9. **Hook shell**: the `VAR=value cmd` form needs Git Bash; production should pass the port another way (e.g. plugin `env` or default port).
10. **Docs** (`README.md`, `docs/session-recovery.md`, `docs/pigeon-wait.md`) still describe only the Monitor path; milestone 4 is pending.

## 9. Environment state after the work

- Development plugin uninstalled; its marketplace and cache removed; isolated services stopped; temporary databases deleted; no Debug `pigeon.exe` processes running; port 49473 free.
- `~/.claude/settings.json`, `C:\Projects\Pigeon\.claude\settings.local.json`, `~/.claude/plugins/installed_plugins.json` and `known_marketplaces.json` match the hashes taken before the re-test (and the earlier backups apart from the official marketplace's own `lastUpdated` refresh).
- `C:\Projects\PigeonHookWaiterTest` exists and is empty (held as the working directory of test conversations).
- Test conversations ("Hook waiter scenario 1, 7a, 7b, 10, 9 re-test", "Resume probe v3", "Stop waiter test conversation") remain idle in the user's Desktop session list.
- No Pigeon production data, Project Sigma or global agent configuration was touched; all messages were synthetic.

## 10. Suggested next steps

1. Review sections 3 and 8, especially the ownership policy and the exit-code contract.
2. Decide the production shape (milestone 5): port configuration without the Bash prefix, whether to keep the sync `SessionStart` context hook (its Monitor instruction needs a waiter-aware variant), and the connect skill's Monitor guidance.
3. Milestone 4 docs.
4. Optional hardening: injectable clock for ownership, hook orphan test, channel release on bridge stop.
