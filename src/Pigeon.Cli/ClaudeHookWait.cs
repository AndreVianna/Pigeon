using Pigeon.Adapters;
using Pigeon.Core;
using Pigeon.Service;
using System.Text.Json;

// A Claude Code hook with asyncRewake wakes the session when this command exits 2 and shows
// its stderr to the model. It emits either one fixed message-wake instruction or, when
// enabled, one fixed idle-renewal instruction. Other outcomes exit 0 without output.
// It never leases a message, and it claims only while it owns the session's wakes.
internal static class ClaudeHookWait {
    public const int DefaultTimeoutSeconds = 570;
    public const int DefaultRenewalSeconds = 43200;

    public static async Task<int> RunAsync(string port, int timeoutSeconds, bool renew) {
        try {
            return await WaitAsync(port, await SessionHook.ReadSessionIdAsync(), TimeSpan.FromSeconds(timeoutSeconds), renew);
        }
        catch (Exception) {
            return 0;
        }
    }

    private static async Task<int> WaitAsync(string port, string sessionId, TimeSpan timeout, bool renew) {
        using var parent = MonitorParent.Capture();
        using var stop = new CancellationTokenSource(timeout);
        var holderId = Guid.NewGuid().ToString("N");
        var operatorPrincipal = new Principal(Mailbox.TopicNamespace, "operator");
        var associated = false;
        Principal? current = null;
        var reason = "expired";
        try {
            while (!stop.IsCancellationRequested) {
                if (!parent.IsAlive) { reason = "orphaned"; break; }
                try {
                    if (!associated) {
                        var route = await LocalHttpClient.ExecuteAsync(port, operatorPrincipal, "session_restore", new { tool = "claude-code", sessionId }, stop.Token);
                        // Unconnected sessions also run this hook after every turn; leave at once.
                        if (!route.GetProperty("Ok").GetBoolean() || route.GetProperty("Value").ValueKind != JsonValueKind.Object) return 0;
                        associated = true;
                    }
                    var bridges = await LocalHttpClient.ExecuteAsync(port, operatorPrincipal, "claude_session_bridges", new { sessionId }, stop.Token);
                    if (!bridges.GetProperty("Ok").GetBoolean()) throw new InvalidOperationException();
                    var matches = bridges.GetProperty("Value").EnumerateArray().Select(bridge => bridge.GetString()!).ToArray();
                    // Never guess if no registered bridge matches or more than one identity claims the session.
                    var selected = matches.Length == 1 ? new Principal(Mailbox.TopicNamespace, matches[0]) : null;
                    if (current is not null && current != selected) {
                        _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_disconnect", new { id = holderId }, stop.Token);
                        _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner_release", new { holderId, reason = "stopped" }, stop.Token);
                    }
                    current = selected;
                    if (current is not null) {
                        _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_heartbeat", new { id = holderId, mode = "claude-hook" }, stop.Token);
                        var ownership = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner", new { holderId, mode = "claude-hook" }, stop.Token);
                        if (ownership.GetProperty("Ok").GetBoolean() && !ownership.GetProperty("Value").GetProperty("owner").GetBoolean()) {
                            // A newer waiter replaced this one, so leave.
                            if (ownership.GetProperty("Value").GetProperty("active").GetString() == "claude-hook") {
                                _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_disconnect", new { id = holderId }, stop.Token);
                                current = null;
                                return 0;
                            }
                            // A Monitor or channel owns the session. Wait without claiming, and take over
                            // once it stops or its ownership lapses, so a killed Monitor leaves no gap.
                            await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
                            continue;
                        }
                        if (!parent.IsAlive) { reason = "orphaned"; break; }
                        var result = await LocalHttpClient.ExecuteAsync(port, current, "wake_claim", new { mode = "claude-hook", sessionId, holderId }, stop.Token);
                        if (result.GetProperty("Ok").GetBoolean() && result.GetProperty("Value").ValueKind == JsonValueKind.Object) {
                            var claim = result.GetProperty("Value");
                            var generation = claim.GetProperty("Generation").GetString()!;
                            var attempt = claim.GetProperty("Attempt").GetInt32();
                            try {
                                await Console.Error.WriteLineAsync(WakePrompt.For(generation));
                                await Console.Error.FlushAsync(CancellationToken.None);
                            }
                            catch (IOException) {
                                reason = "stopped";
                                try {
                                    using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                                    _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_result", new { generation, attempt, outcome = "transport-failed" }, failureTimeout.Token);
                                }
                                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException) { }
                                return 0;
                            }
                            // Once stderr has been written, exit 2 so the host can receive it even
                            // if the service disappears before the transport result is recorded.
                            try {
                                using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                                _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_result", new { generation, attempt, outcome = "transport-written" }, reportTimeout.Token);
                            }
                            catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException) { }
                            reason = "woke";
                            return 2;
                        }
                    }
                }
                catch (Exception e) when (e is IOException or TimeoutException or JsonException or InvalidOperationException or KeyNotFoundException) {
                    // A service restart or a changed session binding is retried without emitting a false wake.
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) {
                    // The HTTP request timed out; keep waiting after the service recovers.
                }
                await Task.Delay(TimeSpan.FromSeconds(1), stop.Token);
            }
            if (stop.IsCancellationRequested) throw new OperationCanceledException(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) {
            // A renewal is a host turn, not a message wake. Only the current live owner
            // can request one; a superseded waiter or inactive session must stay silent.
            if (renew && current is not null && parent.IsAlive) {
                try {
                    using var renewalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var result = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner_check", new { holderId }, renewalTimeout.Token);
                    if (result.GetProperty("Ok").GetBoolean() && result.GetProperty("Value").GetProperty("owner").GetBoolean() && parent.IsAlive) {
                        await Console.Error.WriteLineAsync("Pigeon notification waiter needs renewal. This is not a message. End this turn so the Pigeon Stop hook can re-arm the waiter. Do not poll or send a reply just for this renewal.");
                        await Console.Error.FlushAsync(CancellationToken.None);
                        reason = "renewed";
                        return 2;
                    }
                }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { }
            }
        }
        finally {
            if (current is not null) {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { _ = await LocalHttpClient.ExecuteAsync(port, current, "bridge_disconnect", new { id = holderId }, cleanup.Token); }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException) { }
                try { _ = await LocalHttpClient.ExecuteAsync(port, current, "wake_owner_release", new { holderId, reason }, cleanup.Token); }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException) { }
            }
        }
        return 0;
    }
}
