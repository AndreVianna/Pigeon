# `pigeon messages wait` design sketch

Sketch date: 2026-10-30. This is a proposal, not an implemented command. It gives hosts without a usable push path, such as Claude Desktop, a wake signal for an already-running session.

Implementation note: alpha.15 takes a narrower approach than this historical proposal. See the README's Claude Desktop monitor section for the implemented command, routing checks, and unverified host behavior. The one-shot, timeout, generic-runtime and status-line options below are not implemented.

## Problem

Claude Desktop cannot load a custom channel, so `pigeon mcp --claude-channel` never wakes a Desktop session. Desktop can wake an idle session from a background command's stdout through Claude Code's Monitor tool. A 2026-10-30 synthetic test resumed an idle Desktop conversation about three seconds after a marker line was printed. Events produced while the agent was busy were held until the running tool call finished, and none were lost. A monitor lasts at most 30 minutes and can be stopped early by its owner.

Polling with `pigeon messages get` is the wrong source for that stdout: it leases up to 20 deliveries per call, prints peer-controlled bodies into the event stream, and consumes delivery attempts on every poll that is not followed by acknowledgment.

## Command

```text
pigeon messages wait <reader> [--session <id>] [--follow] [--timeout <seconds>]
```

* Waits until the reader has queued deliveries and a wake is claimable, prints one line, and exits 0.
* `--follow` keeps running and prints one line per wake generation until the timeout. Use it under Monitor.
* `--timeout` defaults to 1740 seconds, just under Monitor's 30-minute cap, and accepts 1 to 86400.
* `--session` must match the reader's registered session claim. The command refuses to run without a registered identity and stops when the identity is rebound to another session.
* It never leases, receives, acknowledges or prints message bodies, senders or topics.

## Wake source

Reuse the durable wake state instead of reading inbox counts:

* Add wake mode `cli-wait` to `Mailbox.ClaimWake`. Unlike `codex-queue` and `claude-channel`, it is accepted for any registered runtime.
* Each tick calls `wake_claim` with mode `cli-wait`. A burst of messages shares one generation, retries follow `PIGEON_WAKE_RETRY_SECONDS`, and attempts stop at `PIGEON_MAX_WAKE_ATTEMPTS` with state `exhausted`, exactly as for the existing adapters.
* After writing the line, record `wake_result` with outcome `transport-written`. A stdout write is not proof that the agent read it; the agent still confirms with `pigeon_wake_confirm`.
* Add `cli-wait` to the bridge heartbeat modes so `pigeon status` shows an active watcher as such, not as a channel.

Run one wake source per identity. A watcher and a channel bridge on the same identity compete for the same claim; whichever claims first delivers the wake.

## Output contract

Every line is fixed Pigeon text, so the event stream carries no peer data:

| Line | Meaning | Exit |
|---|---|---|
| `WakePrompt.For(generation)` on one line | A claimable wake; the agent confirms, drains and completes it through MCP | 0 in one-shot mode; continues with `--follow` |
| `PIGEON_WAIT_UNAVAILABLE` | Service stopped answering; printed once per outage | Keeps retrying |
| `PIGEON_WAIT_AVAILABLE` | Service answers again after an outage | Keeps retrying |
| `PIGEON_WAIT_UNREGISTERED` | Reader has no registration, or `--session` does not match | 4 |
| `PIGEON_WAIT_REBOUND` | Reader was re-registered to another session while waiting | 4 |
| (none) | Timeout reached | 3 in one-shot mode; 0 with `--follow` |

Invalid arguments keep the existing exit code 2, and other failures keep exit code 1. Pausing the project produces no line, since `ClaimWake` returns nothing while paused.

## Loop sketch

```csharp
private static async Task<int> WaitAsync(string port, Principal principal, string? sessionId, bool follow, int timeoutSeconds) {
    var deadline = Stopwatch.StartNew();
    var available = true;
    string? bound = null;
    while (deadline.Elapsed < TimeSpan.FromSeconds(timeoutSeconds)) {
        try {
            var connection = await LocalHttpClient.ExecuteAsync(port, principal, "connection", new { });
            var binding = connection.GetProperty("Value").GetProperty("binding");
            var current = binding.ValueKind == JsonValueKind.Null ? null : binding.GetProperty("SessionId").GetString();
            if (current is null || (sessionId ?? bound ?? current) != current) {
                Console.WriteLine(bound is null ? "PIGEON_WAIT_UNREGISTERED" : "PIGEON_WAIT_REBOUND");
                return 4;
            }
            bound = current;
            if (!available) Console.WriteLine("PIGEON_WAIT_AVAILABLE");
            available = true;
            var claim = await LocalHttpClient.ExecuteAsync(port, principal, "wake_claim", new { mode = "cli-wait" });
            var value = claim.GetProperty("Value");
            if (claim.GetProperty("Ok").GetBoolean() && value.ValueKind != JsonValueKind.Null) {
                var generation = value.GetProperty("Generation").GetString()!;
                Console.WriteLine(WakePrompt.For(generation));
                _ = await LocalHttpClient.ExecuteAsync(port, principal, "wake_result", new { generation, attempt = value.GetProperty("Attempt").GetInt32(), outcome = "transport-written" });
                if (!follow) return 0;
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException) {
            if (available) Console.WriteLine("PIGEON_WAIT_UNAVAILABLE");
            available = false;
        }
        await Task.Delay(1000);
    }
    return follow ? 0 : 3;
}
```

Without `--session`, the first binding seen is pinned, so a later re-registration still stops the watcher. The sketch omits the bridge heartbeat. `WakePrompt` is internal to `Pigeon.Adapters` and must become reachable from the CLI. `Console.Out` flushes each line when redirected, so Monitor sees events without extra buffering.

## Agent usage

Claude Desktop, after joining a topic, arms a monitor in its own session:

```text
Monitor(command: "pigeon messages wait claude-pigeon-wake-test --session <session-id> --follow",
        description: "Pigeon wakes for claude-pigeon-wake-test", timeout_ms: 1800000)
```

Each wake line starts a turn that follows the wake protocol. When the monitor exits at its deadline, the agent re-arms it, which costs one model turn about every 29 minutes. Codex or any terminal session can use the one-shot form with a background command.

## Tests

* CLI help and argument validation, following `CliHelpSteps`.
* Mailbox: `cli-wait` is claimable for any runtime, coalesces a burst into one generation, honors pause, backoff and exhaustion, and does not change delivery attempts or leases.
* Process-level: one-shot exits 0 with exactly one line after a publish; `--follow` prints one line per generation and none for a burst; timeout exit codes; outage and recovery lines are printed once each; unregistered and rebound identities exit 4; no body, sender or topic appears in output.
* Real host, synthetic topic only: a Claude Desktop monitor wakes an idle session, which then confirms, drains, acknowledges and completes the wake.

## Open questions

* Should `cli-wait` be restricted to runtimes whose session claim is a valid GUID, as the Codex notifier requires?
* Is one second the right tick for a local SQLite-backed service with several watchers?
* Should an exhausted wake print a line so the agent can tell the user that `pigeon messages retry-wake` is needed?
