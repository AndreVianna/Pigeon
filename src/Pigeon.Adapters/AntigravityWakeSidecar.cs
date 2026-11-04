using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using Pigeon.Service;

namespace Pigeon.Adapters;

// Runs as an Antigravity App sidecar and wakes idle Antigravity conversations with
// `agentapi send-message`. The service decides when a session may be woken (it tracks turns
// through the plugin's hooks); the sidecar only sends. Acceptance by agentapi is not delivery:
// a message to a deleted conversation is accepted and lost, so the wake's own retries apply.
public sealed class AntigravityWakeSidecar {
    public const string Mode = "antigravity-agentapi";
    private readonly Func<string, object, CancellationToken, Task<JsonElement>> broker;
    private readonly Func<string, string, CancellationToken, Task<bool>> send;

    public AntigravityWakeSidecar(string port, string agentApi)
        : this((op, args, token) => LocalHttpClient.ExecuteAsync(port, op, args, token), (session, message, token) => SendAsync(agentApi, session, message, token)) { }

    public AntigravityWakeSidecar(Func<string, object, CancellationToken, Task<JsonElement>> broker, Func<string, string, CancellationToken, Task<bool>> send) {
        this.broker = broker;
        this.send = send;
    }

    // Returns how many wakes were sent.
    public async Task<int> TickAsync(CancellationToken cancellation) {
        var states = await broker("wake_states", new { }, cancellation);
        if (!states.GetProperty("Ok").GetBoolean())
            return 0;
        var sent = 0;
        foreach (var item in states.GetProperty("Value").EnumerateArray()) {
            if (item.GetProperty("Tool").GetString() != "antigravity")
                continue;
            var id = item.GetProperty("SessionId").GetString()!;
            var claim = await broker("wake_claim", new { sessionId = id, mode = Mode }, cancellation);
            if (!claim.GetProperty("Ok").GetBoolean() || claim.GetProperty("Value").ValueKind == JsonValueKind.Null)
                continue;
            var value = claim.GetProperty("Value");
            var generation = value.GetProperty("Generation").GetString()!;
            var attempt = value.GetProperty("Attempt").GetInt32();
            var accepted = await send(id, WakePrompt.For(generation), cancellation);
            _ = await broker("wake_result", new { sessionId = id, generation, attempt, outcome = accepted ? "transport-accepted" : "transport-failed" }, cancellation);
            sent++;
        }

        return sent;
    }

    // Keeps running while the service is down; Antigravity restarts the sidecar if it exits.
    public async Task RunAsync(CancellationToken cancellation) {
        var bridgeId = Guid.NewGuid().ToString("N");
        var lastHeartbeat = long.MinValue;
        while (!cancellation.IsCancellationRequested) {
            try {
                if (Environment.TickCount64 - lastHeartbeat >= 3000) {
                    _ = await broker("bridge_heartbeat", new { id = bridgeId, mode = Mode }, cancellation);
                    lastHeartbeat = Environment.TickCount64;
                }

                _ = await TickAsync(cancellation);
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException) { }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }

            await Task.Delay(1000, cancellation);
        }
    }

    // ANTIGRAVITY_AGENTAPI_EXE names the language server, which takes `agentapi` as its first
    // argument; an `agentapi` program takes the subcommand directly.
    private static async Task<bool> SendAsync(string agentApi, string sessionId, string message, CancellationToken cancellation) {
        var info = new ProcessStartInfo(agentApi) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileName(agentApi).StartsWith("language_server", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add("agentapi");
        foreach (var argument in new[] { "send-message", sessionId, message })
            info.ArgumentList.Add(argument);
        try {
            using var process = Process.Start(info);
            if (process is null)
                return false;
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var error = process.StandardError.ReadToEndAsync(cancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                if (cancellation.IsCancellationRequested)
                    throw;
                return false;
            }

            await Task.WhenAll(output, error);
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException) {
            return false;
        }
    }
}
