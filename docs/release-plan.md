# Release plan: 0.1.0

Draft of 2026-10-31 for review. Nothing in it is approved or started. It covers every change and test between the current branch (`cleanup-and-wake-fixes`, version 0.1.0-alpha.19) and a 0.1.0 release.

## What 0.1.0 should promise

A Codex session and a Claude Desktop conversation on the same Windows machine exchange durable messages without the user copying them, and each wakes the other while its conversation process is running. The following stay documented limits, not release promises:

- no application-level authentication;
- at-least-once delivery (duplicates possible, never exactly-once);
- no wake for a conversation whose process Desktop has not started (after a reopen or app restart).

## Where we start

- **Shown on the host:** an alpha.18 two-way exchange between Claude Desktop and Codex; 12 Desktop scenarios with the development plugin: idle and busy turns, bursts, a service restart and notifier takeover, with 21 of 21 messages acknowledged once.
- **Automated:** 22 unit tests and 151 integration scenarios pass on the branch (after Phase 1).
- **Never run on the host:** the packaged 0.4.0 plugin; the 12-hour renewal; Codex Desktop recovery. (This branch's code has since passed H1a, H2 and H3 with a dev plugin generated from the 0.4.0 hooks.)
- **Known open defect (fixed since in 1.1):** a hook waiter could outlive its shell and swallow a wake.

## Phase 1: code changes

Each item gets automated tests and keeps the full suite green.

**Status (2026-10-31):** 1.1, 1.2A, 1.3 and 1.4 are done on `cleanup-and-wake-fixes`. 1.2B and 1.5 are deferred until after the release.

| Item | Importance | Status |
|---|---|---|
| 1.1 Ancestor-chain lifetime | Critical (blocker) | Done. An integration scenario kills only the outer Git Bash process; the waiter exits as `orphaned` and claims nothing. A manual rerun of the earlier reproduction went from "survived, claimed 1 attempt" to "exited, 0 attempts". |
| 1.2A Renew during an outage | High (blocker) | Done. A waiter that owned the session at its last check renews when the service is unreachable at the deadline; its replacement cannot repeat that until it reaches the service. |
| 1.3 Ownership races | Medium | Done. Tests only; no code change was needed. |
| 1.4 Status diagnostics | Medium | Done, more simply than planned. Status prints the service version and warns when it differs from the CLI. A Claude session with no notifier since the service started is flagged with a hint to check the plugin and CLI. No time threshold was added. |
| 1.2B Standby renewal | Low | Deferred. |
| 1.5 Stale Codex prompts | Low | Deferred. |

### 1.1 Waiter tracks its ancestors (release blocker)

**Problem.** `MonitorParent` watches only the waiter's immediate parent. Under Git Bash that is an intermediate `bash.exe`, which survives when only the hook's outer process is killed. The waiter then claims a wake and writes it to an orphaned pipe.

**Change.** At startup the waiter walks its own ancestor chain, at most 8 levels, using `NtQueryInformationProcess`. It stops at the first process that is not a shell (`bash`, `sh`, `cmd`, `powershell`, `pwsh`) or at the first `claude.exe`, and holds a handle to every process on the way. While waiting, all held ancestors must be alive.

- **PID reuse.** Each ancestor's creation time must be earlier than its child's. A reused PID breaks that order, so the walk stops there.
- **Access denied.** The walk stops at a process it cannot open.
- **Fail closed.** If it finds a `claude.exe` ancestor but cannot hold it, the waiter exits without claiming.
- **Fallback.** With no Claude ancestor (CLI use, the test harness, a Monitor), behavior stays as it is today: the immediate parent only.

**Tests.**

- An integration scenario builds a real chain (test → `bash -c` → `bash` → `pigeon hook wait`), kills only the outer process, and checks that the waiter exits as `orphaned` within about 2 seconds and that a message sent afterwards consumes no wake attempt.
- A unit test of the stop rules with a fake process table: PID reuse, access denied, depth limit.
- The test inspects and kills only processes it started itself.

### 1.2 Close the renewal gaps (release blocker)

**Gap A: service unavailable at the deadline.** Ownership cannot be confirmed, so no renewal is requested and nothing re-arms until the next turn.

- **Change:** if the waiter owned the session within the last minute and the service is unreachable at the deadline, request the renewal anyway. This stays bounded: at most one renewal turn per deadline.
- **Test:** stop the service just before a short deadline and check that one renewal line is emitted.

**Gap B: standby waiter expires behind another notifier.** If a Monitor or channel owns the session, the standby hook waiter expires silently; if that notifier then disappears, nothing waits.

- **Change:** at the deadline, a standby waiter for a session that still has subscriptions also requests a renewal. This costs at most 2 extra turns a day, and only in the rare Monitor-plus-hook setup.
- **Test:** a short-deadline waiter behind a monitor emits a renewal; with no subscriptions it stays silent, as today.

**Decision for you:** both changes add model turns in edge cases. The alternative is to keep the gaps and document them.

### 1.3 Ownership races after a service restart

- Add integration scenarios:
  - two hook waiters and a monitor reconnect after a service restart, and exactly one owns the session;
  - a waiter whose owner lapsed (no heartbeat for 15 seconds) is replaced without a duplicate wake.
- Code changes only if these tests fail.

### 1.4 Diagnostics for an incompatible or missing CLI

The wrapper stays silent on usage errors so they can't cause wake loops, but that hides a wrong install.

- **Change:** `pigeon status` reports the CLI version and, for each Claude session, when its hook waiter was last seen. A session with a bridge but no waiter seen for more than one renewal period is flagged.
- **Test:** a status scenario.

### 1.5 Repeated stale Codex wake prompts (not a blocker)

Investigate whether the Codex queue interface supports cancelling or replacing a queued prompt. If it doesn't, keep the generation checks, document the limit, and defer.

## Phase 2: release engineering

| Item | Change |
|---|---|
| Version | CLI `0.1.0`. Claude plugin `0.4.1` (its packaged files changed). Codex plugin `0.3.0` (done: moved to the layout Codex loads). Update the README `dotnet tool update` line and the version mentions in docs. |
| Codex plugin install | Repo marketplace `.agents/plugins/marketplace.json` added (and copied into the CLI's publish output). Done: the production Codex plugin moved to the layout Codex loads local servers from (`.codex-plugin/plugin.json` with `"mcpServers": "./.mcp.json"`, and a `.mcp.json` of `command`/`args`/`env`), as proven by the dev plugin in H1b; a root `mcp.json` with `"type": "stdio"` is ignored. It is now `0.3.0`. Still to do: reinstall it as the `pigeon` plugin instead of the standalone MCP server it was configured as. `mcp_tool` hooks work in Codex and stay. |
| CI | GitHub Actions on `windows-latest`: SDK from `global.json`, then build, test and pack, plus a Git Bash check. Verify that the runner can install the .NET 11 RC SDK and has `winsqlite3`. |
| Secret scanning | Optional `ggshield` step in CI. It needs a GitGuardian API key stored as a repository secret, which you would create. |
| Release notes | `CHANGELOG.md` for 0.1.0: features, fixes since alpha.19, and documented limits. |
| Docs | Install-from-scratch instructions in the README, checked on a clean tool path; refreshed known issues; `next-steps.md` folded into this plan or retired. |
| Upgrade | Install the packed 0.1.0 into a temporary `--tool-path` and run it against a synthetic older-schema database. Optionally, with your approval, a copy of your current database, to confirm migration without touching the live one. |
| Tag and publish | Merge to `main`, tag `v0.1.0`, create a GitHub release with the `.nupkg`. Publishing to nuget.org is a separate decision. |

## Phase 3: live host tests

These run after Phase 1. Every one needs your approval, uses an isolated service on its own port with a disposable database, uses new test conversations and synthetic messages, and never touches Project Sigma or existing sessions.

Earlier, the auto-mode classifier blocked me from starting a test conversation. For these tests you open the new conversations; I prepare everything else and drive the messages.

**Status (2026-10-31):** H1a (Claude with a scripted peer), H2 and H3 passed with the branch build; details and timings are in [the host-test runbook](../experiments/host-test/README.md#results-2026-10-31). H1b (Claude with Codex) passed later the same day with the dev Codex plugin, after its packaging was fixed (see below). H2's renewal turn still needs a visual check that it ended without polling or replying. H4, H5 and H6 have not run. Finding: on the host the hook launcher exits at once, so the waiter's chain ends at the top Git Bash shell and does not reach `claude.exe` (see [known issues](known-issues.md)).

| # | Test | Setup | Pass criteria | Duration |
|---|---|---|---|---|
| H1 | Smoke test of the branch build | Dev plugin rebuilt from the branch; its hooks are a byte-for-byte copy of production 0.4.1 apart from port and binary overrides, including the direct `hook-wait.sh` invocation. One new Claude Desktop conversation and one new Codex chat. | A two-way exchange with no user action: each message is woken, confirmed, received, acknowledged and completed exactly once. | about 15 min |
| H2 | Renewal and re-arm | Same, with a 2-minute deadline. | One renewal turn with no message activity; Stop re-arms; a message sent afterwards wakes the replacement waiter; status shows one notifier. | about 15 min |
| H3 | Orphan fix on the host | Same; I kill only the hook's outer process. | The waiter exits as orphaned; the next message is delivered on the next turn with no lost attempt. | about 10 min |
| H4 | 12-hour timeout | Production timeouts against the isolated service, left idle overnight. | Exactly one renewal after about 12 hours, and a morning message wakes the conversation. | overnight, unattended |
| H5 | Codex Desktop recovery | New isolated Codex conversation with the plugin; tested idle, busy, after closing and reopening, and after an app restart. | The hook-trust prompt appears once; restore binds the session; queued messages are delivered when the session is running. Each state is reported separately. | about 30 min |
| H6 | Claude Desktop reopen | Archive and reopen the H1 conversation; restart the app. | Behavior observed and documented. Not a pass/fail gate, because it depends on Desktop. | about 15 min |

The release gate is H1 to H5 passing. If H4 fails because Desktop cuts the hook early, the fallback is a shorter renewal period (for example 1 hour, at up to 24 short turns a day per idle session). That would be a decision for you, followed by a repeat of H4 at the new period.

## Sequence and effort

1. Phase 1.1 and 1.2, about a day including tests, then 1.3 and 1.4, about half a day.
2. H1 to H3 in one session of about an hour, then H4 overnight.
3. H5 in a separate session.
4. Phase 2, about half a day, then merge, tag and release.

Phase 1.5 and the code simplifications in `next-steps.md` (removing non-topic mode, sharing the waiter and monitor loop, merging the send and publish guards) come after the release.

## Decisions needed from you

1. Close renewal gaps A and B, which adds a few renewal turns in edge cases, or keep them as documented limits.
2. Whether a ggshield CI step is wanted, which needs a GitGuardian API key.
3. Whether to test migration against a copy of your current database.
4. Whether to publish to nuget.org or only attach the package to a GitHub release.
5. The fallback if H4 fails: a shorter renewal period, or document the 12-hour limit.
6. Approval to run H1 to H6, with you opening the test conversations.
