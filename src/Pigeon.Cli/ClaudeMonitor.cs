using Pigeon.Adapters;
using Pigeon.Core;
using Pigeon.Service;
using System.Text.Json;

// The monitor only emits a fixed wake instruction. It never leases a message.
internal static class ClaudeMonitor {
    public static async Task<int> RunAsync(string port, string[] args) {
        var automatic = args[1] == "--claude";
        var sessionId = automatic ? (args.Length == 3 ? args[2] : Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ID")) : args[2];
        if (string.IsNullOrWhiteSpace(sessionId)) {
            Console.Error.WriteLine("Pigeon monitor needs CLAUDE_CODE_SESSION_ID; this host did not provide it. Start an explicit session monitor instead.");
            return 1;
        }
        using var parent = MonitorParent.Capture();
        var monitorId = Guid.NewGuid().ToString("N");
        var operatorPrincipal = new Principal(Mailbox.TopicNamespace, "operator");
        Principal? current = null;
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try {
            while (!stop.IsCancellationRequested && parent.IsAlive) {
                try {
                    var bridges = await LocalHttpClient.ExecuteAsync(port, operatorPrincipal, "claude_session_bridges", new { sessionId }, stop.Token);
                    if (!bridges.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
                    var matches = bridges.GetProperty("Value").EnumerateArray().Select(bridge => bridge.GetString()!)
                        .Where(agent => automatic || agent == args[1]).ToArray();
                    // Never guess if no registered bridge matches or more than one identity claims the session.
                    var selected = matches.Length == 1 ? new Principal(Mailbox.TopicNamespace, matches[0]) : null;
                    if (current is not null && current != selected) {
                        _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_disconnect", new { id = monitorId }, stop.Token);
                        _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner_release", new { holderId = monitorId, reason = "stopped" }, stop.Token);
                    }
                    current = selected;
                    if (current is not null) {
                        _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_heartbeat", new { id = monitorId, mode = "claude-monitor" }, stop.Token);
                        if (!parent.IsAlive) break;
                        // Another notifier, such as a channel or hook waiter, may own this session's wakes.
                        var ownership = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner", new { holderId = monitorId, mode = "claude-monitor" }, stop.Token);
                        if (!ownership.GetProperty("Ok").GetBoolean() || !ownership.GetProperty("Value").GetProperty("owner").GetBoolean()) {
                            await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
                            continue;
                        }
                        var result = await LocalHttpClient.ExecuteAsync(port, current, "wake_claim", new { mode = "claude-monitor", sessionId, holderId = monitorId }, stop.Token);
                        if (result.GetProperty("Ok").GetBoolean() && result.GetProperty("Value").ValueKind == JsonValueKind.Object) {
                            var claim = result.GetProperty("Value");
                            var generation = claim.GetProperty("Generation").GetString()!;
                            var attempt = claim.GetProperty("Attempt").GetInt32();
                            try {
                                await MonitorOutput.WriteLineAsync(WakePrompt.For(generation), stop.Token);
                            }
                            catch (IOException) {
                                // The wake may have been partially written; record failure but never keep claiming wakes without a listener.
                                try { _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_result", new { generation, attempt, outcome = "transport-failed" }, stop.Token); }
                                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
                                return 1;
                            }
                            _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_result", new { generation, attempt, outcome = "transport-written" }, stop.Token);
                        }
                    }
                }
                catch (Exception e) when (e is IOException or TimeoutException or JsonException or InvalidOperationException) {
                    // A service restart or a changed session binding is retried without emitting a false wake.
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) {
                    // The HTTP request timed out; keep watching after the service recovers.
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally {
            if (current is not null) {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_disconnect", new { id = monitorId }, cleanup.Token); }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
                try { _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner_release", new { holderId = monitorId, reason = "stopped" }, cleanup.Token); }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
            }
        }
        return 0;
    }
}
