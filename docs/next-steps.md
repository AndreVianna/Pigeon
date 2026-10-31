# Status and proposed next steps

Assessment of 2026-10-31, after Claude took over the project from Codex. Proposals only: nothing here is approved or scheduled. [Known issues](known-issues.md) is the authoritative list of defects and limits.

## Where the project stands

- **Code.** About 3,000 lines of C# in four projects (Core, Service, Adapters, CLI), version 0.1.0-alpha.19. The build has no warnings. 12 xUnit unit tests and 146 Gherkin/Reqnroll integration scenarios pass; the integration scenarios start real service, MCP and CLI processes against disposable databases.
- **Demonstrated on the host.** With alpha.18, one synthetic two-way exchange between a Claude Desktop conversation (hook waiter) and a Codex chat (queue adapter) completed without user intervention. A development plugin also passed 12 Desktop scenarios: idle and busy turns, bursts, service restart, notifier takeover, and selected reopen cases.
- **Not yet demonstrated.** Alpha.19's idle renewal in a live Desktop conversation, Desktop honoring the 12-hour hook timeout, the production plugin's direct `hook-wait.sh` invocation in Desktop, Codex Desktop recovery, and reliable delivery after Desktop reopen or app restart.
- **Installed on this machine.** CLI alpha.19, the service running on the default port, Claude plugin 0.4.0.

## Proposed next steps

### 1. Close the orphaned-waiter gap (no host test needed)

The four other confirmed bugs from the review were fixed on 2026-10-31 (see the end of this document). The remaining one is to make the hook waiter watch its ancestors, not just its immediate parent. Requirements carried over from the Codex review:

- walk only this process's own ancestors, hold handles, and treat PID reuse and access denial as "cannot track";
- fail closed if a Claude ancestor is found but cannot be held;
- keep the current immediate-parent behavior when there is no Claude ancestor (CLI, tests, Monitor);
- add an isolated test that kills only the outer shell.

### 2. Verify on the host (each needs your approval)

1. **Short renewal test in Claude Desktop** with the production 0.4.0 hooks, against an isolated service on another port. Use a short-deadline copy (`experiments/claude-hook-waiter-dev`, 0.4.0-dev.1). Check that the renewal turn starts, Stop re-arms a waiter, a later synthetic message is delivered through the replacement, and only one notifier exists. The earlier attempt was stopped when Claude's auto-mode classifier refused to start the test conversation, with no stated reason. Starting it needs your explicit go-ahead or your own click.
2. **One long-timeout run** to confirm Desktop honors a hook timeout of 12 hours and 2 minutes, before relying on unattended operation.
3. **Codex Desktop recovery**: hook trust, server name and restore on resume, in a new isolated Codex conversation.

### 3. Simplify (after the fixes above, behind the existing tests)

1. Remove the legacy non-topic code paths and `*ForProject` twins from `Mailbox`, keeping only the one-time migration.
2. Share one wake loop between `ClaudeHookWait` and `ClaudeMonitor`; they repeat about 60 lines of bridge selection, ownership, claim and result reporting.
3. Merge the duplicated send and publish guards (expiry, pause, automated-exchange and rate limits) into shared helpers.
4. Merge the duplicated route-binding logic in `McpSession`.

### 4. Repository hygiene

1. **GitGuardian alert.** No credentials were found in either commit. The likely trigger is the random 32-hex-character lock IDs, in a field named `token`, in two probe logs under `experiments/claude-resume-probe/logs/`. Confirm the flagged file, then mark it a false positive. Rewriting history would need a force-push.
2. **Continuous integration.** There is none. A GitHub Actions workflow on a Windows runner with the pinned .NET 11 SDK and Git Bash would run the build and tests on every push.
3. **Renewal cost.** Each renewal is a turn on the conversation's own model. No supported way was found for a cheaper model to act inside an existing conversation; keeping the renewal turn minimal is the available lever.

## Cleanup done in this pass (uncommitted)

- Moved dated research, plans, reports and the overnight handoff to `docs/history/`, fixed links, and added a documentation index to the README and an index of `experiments/`.
- Removed `tools/build-offline.ps1`, which could no longer build the solution: it referenced a deleted `LocalPipe.cs` and omitted most current sources.
- Moved the unpackaged PowerShell hook wrapper from the production plugin folder to `experiments/claude-hook-waiter-dev/`.
- Replaced the development plugin's temporary-folder binary path with the repository's Debug build.
- Code: the MCP bridge's channel loop no longer stops permanently after a service timeout or a malformed response; hook-waiter and monitor cleanup now release notifier ownership even if disconnect fails, and tolerate HTTP timeouts; `pigeon start` no longer kills a starting service when one readiness probe times out; the MCP server version now comes from the package version instead of a hard-coded string; removed three unused members and a duplicate port validator.
- Docs: corrected the wake-state list, exhausted-wake behavior, and the scope of `PIGEON_MESSAGE_TTL_SECONDS` in the README.

## Bugs fixed in the second pass (uncommitted)

- **Storage failures are reported separately.** Transient SQLite errors (busy, locked, I/O, disk full, cannot open) return `storage unavailable` instead of `operation rejected`; the client turns that into the same exception as a service outage, so hook waiters, bridge restore and the session hook retry. The SQLite lock wait is now 2 seconds, below the 3-second client timeout, so a held lock is reported rather than timing out. A transaction that SQLite already rolled back is no longer rolled back again.
- **Exhausted wakes re-arm for new messages.** A newly stored message gives an exhausted recipient one fresh generation and attempt budget; a burst shares it.
- **One tool-name normalizer.** Wake claims, the Claude bridge lookup, the channel adapter, status and MCP restore all use `Mailbox.ToolName`, so legacy identities stored as `claude` or `Claude Code` can be woken.
- **Retention sweep throttled** to at most once per minute per project (or once per retention period, if shorter).
- New scenarios: exhausted-wake re-arm, legacy runtime wake claims, a wake claim under a held database lock, and a hook waiter that keeps waiting through a held lock and then wakes.
