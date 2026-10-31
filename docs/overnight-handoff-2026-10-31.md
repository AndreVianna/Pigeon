# Pigeon overnight handoff — 2026-10-31

This is a historical record of the overnight work. The user subsequently reported updating the service and plugin; installed runtime state may therefore differ from the machine state recorded below. See [known issues and technical debt](known-issues.md) for the current follow-up list.

## Outcome

Alpha.19 adds an opt-in renewal mode to the Claude hook waiter. Plugin 0.4.0 uses it: after 12 idle hours, the current waiter asks Claude for a short renewal turn and exits. The next Stop hook is expected to arm a replacement. This does not lease or acknowledge messages. An idle connected session can therefore use up to two renewal model turns per day, using that session's model.

Only the current hook owner may request a renewal. A waiter superseded by another hook, or waiting behind a Monitor or channel, does not issue one. The plugin permits 120 seconds between the waiter's 12-hour deadline and the host hook timeout. Status records a successful release as `renewed`. The unmodified CLI default remains a silent 570-second timeout unless `--renew` is specified.

## Verification

- Full solution: 12 unit tests and 142 Gherkin integration scenarios passed using `dotnet test Pigeon.slnx --no-restore`.
- The new scenarios use a 10-second deadline and disposable service to check renewal without a message claim, delivery through a replacement waiter, silence while a different notifier owns the session, and silence after an idle session unsubscribes. The status test checks that an abandoned owner is reported lost without another claimant.
- Claude's plugin validator passed. Alpha.19 was packed and installed in a temporary workspace folder to check the CLI version, packaged hook definitions and wrapper execution; that temporary installation was removed. A Gherkin scenario covers the wrapper's valid wake and renewal output, old-CLI usage errors and malformed output. An actual alpha.17 CLI was also verified to fail silently through the Bash wrapper. The PowerShell alternative was blocked by this environment's execution policy and is not included in the package; the active hook uses Git Bash.
- Alpha.18 had previously completed a real two-way synthetic exchange with Claude Desktop and Codex without user intervention. That test did not use the new renewal code.

## What remains unverified

- No live Claude Desktop test has exercised alpha.19's renewal and re-arm, and no 12-hour hook run has established that Desktop honors this timeout. The tests compress the Pigeon deadline but do not simulate the host's model-turn behavior.
- If the service is unavailable at the renewal deadline, the current waiter does not request a renewal. Host failure to deliver the turn or run Stop also leaves a gap. Messages remain queued. A waiter behind another notifier can expire without a replacement, and a later failure of that notifier may therefore leave a gap.
- Claude independently reproduced an orphan risk: killing only the outer Git Bash hook process can leave an intermediate shell and Pigeon waiter alive. That waiter can consume a wake whose output has no listener. The current parent check tracks only its immediate parent; an ancestor-lifetime fix is not implemented or tested. This applies to direct and wrapped hooks. Claude Desktop was previously observed killing the whole hook tree on timeout, but that does not establish behavior for every failure.
- Reopening an archived conversation or restarting Desktop does not guarantee that Desktop starts the conversation process. A non-running conversation cannot be woken by its hook.
- Claims are not authenticated; retries may result in duplicate wake prompts or deliveries. Do not infer exactly-once delivery.

## Collaboration and machine state

Codex sent Claude a Pigeon request to review the renewal design. Claude initially required authorization directly in its own conversation; after the user gave it, Claude reviewed the design and identified version-skew, timeout, load and process-lifetime concerns. Claude prepared an isolated Desktop renewal test, but its auto-mode classifier denied starting that test conversation as dangerous without a further explanation. The real-host renewal test has not run. Later follow-up requests remained queued because Claude's test session had no active notifier; those include an ancestor-fix request and a later message superseding it. No ancestor fix by Claude should be assumed, and if the test session resumes it should read the queued coordination messages together before acting.

The alpha.19 package is `artifacts/Pigeon.Cli.0.1.0-alpha.19.nupkg`. The installed tool, live service and agent configurations were not upgraded or reconfigured for this work. The existing service stored the collaboration requests and Claude's reply on the test topic; code tests used disposable databases. A live test requires installing alpha.19 and updating the Claude plugin to 0.4.0, followed by a short-timeout isolated host test before relying on the 12-hour setting. No Sigma session was accessed.
