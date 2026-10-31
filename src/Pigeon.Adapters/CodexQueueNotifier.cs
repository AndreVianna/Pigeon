using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;

namespace Pigeon.Adapters;

// Queue acceptance is not a wake confirmation or a message acknowledgment.
public sealed class CodexQueueNotifier {
    private readonly Func<Principal> principal;
    private readonly Func<string?> sessionId;
    private readonly Func<Principal, string, object, CancellationToken, Task<JsonElement>> broker;
    private readonly Func<string, string, CancellationToken, Task<bool>> enqueue;

    public CodexQueueNotifier(string port, Func<Principal> principal, Func<string?> sessionId)
        : this(principal, sessionId, (p, op, args, token) => LocalHttpClient.ExecuteAsync(port, p, op, args, token), QueueAsync) { }

    public CodexQueueNotifier(Func<Principal> principal, Func<string?> sessionId,
        Func<Principal, string, object, CancellationToken, Task<JsonElement>> broker,
        Func<string, string, CancellationToken, Task<bool>> enqueue) {
        this.principal = principal;
        this.sessionId = sessionId;
        this.broker = broker;
        this.enqueue = enqueue;
    }

    public async Task<McpOutboundNotification?> TickAsync(CancellationToken cancellation) {
        var p = principal();
        var id = sessionId();
        if (!Guid.TryParse(id, out _)) return null;
        var connection = await broker(p, "connection", new { }, cancellation);
        if (!connection.GetProperty("Ok").GetBoolean()) return null;
        var binding = connection.GetProperty("Value").GetProperty("binding");
        if (binding.ValueKind == JsonValueKind.Null ||
            !string.Equals(binding.GetProperty("Runtime").GetString(), "codex", StringComparison.OrdinalIgnoreCase) ||
            binding.GetProperty("SessionId").GetString() != id) return null;
        var claim = await broker(p, "wake_claim", new { mode = "codex-queue" }, cancellation);
        if (!claim.GetProperty("Ok").GetBoolean() || claim.GetProperty("Value").ValueKind == JsonValueKind.Null) return null;
        var value = claim.GetProperty("Value");
        var generation = value.GetProperty("Generation").GetString()!;
        var attempt = value.GetProperty("Attempt").GetInt32();
        var accepted = await enqueue(id!, WakePrompt.For(generation), cancellation);
        _ = await broker(p, "wake_result", new { generation, attempt, outcome = accepted ? "transport-accepted" : "transport-failed" }, cancellation);
        return null;
    }

    private static async Task<bool> QueueAsync(string sessionId, string message, CancellationToken cancellation) {
        var bundled = Environment.GetEnvironmentVariable("CODEX_APP_RUNTIME_CLI_PATH");
        var executable = !string.IsNullOrWhiteSpace(bundled) && Path.IsPathFullyQualified(bundled) && File.Exists(bundled) ? bundled : "codex";
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "queue", "--thread", sessionId, "--message", message }) info.ArgumentList.Add(argument);
        try {
            using var process = Process.Start(info);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var error = process.StandardError.ReadToEndAsync(cancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                if (cancellation.IsCancellationRequested) throw;
                return false;
            }
            await Task.WhenAll(output, error);
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException) { return false; }
    }
}
