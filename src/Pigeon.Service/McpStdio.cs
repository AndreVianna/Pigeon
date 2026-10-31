namespace Pigeon.Service;

public sealed record McpOutboundNotification(string Json, Func<CancellationToken, Task>? Written = null);

// The stdio bridge has no database path and never starts a private service.
public static class McpStdio {
    public static async Task RunAsync(TextReader input, TextWriter output, McpSession session, CancellationToken cancellation = default, Func<CancellationToken, Task<McpOutboundNotification?>>? nextNotification = null, string notificationMode = "claude-channel") {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var writeGate = new SemaphoreSlim(1, 1);
        async Task WriteAsync(string line, CancellationToken token) {
            await writeGate.WaitAsync(token);
            try {
                await output.WriteLineAsync(line.AsMemory(), token);
                await output.FlushAsync(token);
            }
            finally { _ = writeGate.Release(); }
        }
        async Task NotificationsAsync() {
            while (!stopped.IsCancellationRequested) {
                await Task.Delay(500, stopped.Token);
                if (!session.IsReady || nextNotification is null)
                    continue;
                McpOutboundNotification? notification;
                try { notification = await nextNotification(stopped.Token); }
                catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException) { continue; }
                catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { continue; }
                if (notification is null)
                    continue;
                await WriteAsync(notification.Json, stopped.Token);
                if (notification.Written is not null) {
                    try { await notification.Written(stopped.Token); }
                    catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                }
            }
        }
        var bridgeId = Guid.NewGuid().ToString("N");
        async Task HeartbeatAsync() {
            while (!stopped.IsCancellationRequested) {
                if (session.IsReady) {
                    try {
                        await session.TryRestoreAsync(stopped.Token);
                        _ = await session.BridgeAsync("bridge_heartbeat", new { id = bridgeId, mode = nextNotification is null ? "mcp" : notificationMode, restoration = session.RestoreState }, stopped.Token);
                    }
                    catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                    catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { }
                }
                await Task.Delay(3000, stopped.Token);
            }
        }
        var heartbeat = HeartbeatAsync();
        var notifications = nextNotification is null ? Task.CompletedTask : NotificationsAsync();
        try {
            while (!stopped.IsCancellationRequested) {
                var line = await ReadLineAsync(input, stopped.Token);
                if (line is null)
                    return;
                var reply = await session.HandleAsync(line, stopped.Token);
                if (reply is not null)
                    await WriteAsync(reply, stopped.Token);
            }
        }
        finally {
            stopped.Cancel();
            try { await notifications; }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            try { await heartbeat; }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            try {
                using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                _ = await session.BridgeAsync("bridge_disconnect", new { id = bridgeId }, disconnectTimeout.Token);
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException or OperationCanceledException) { }
        }
    }

    private static async Task<string?> ReadLineAsync(TextReader input, CancellationToken cancellation) {
        const int limit = 65536;
        var text = new System.Text.StringBuilder();
        var character = new char[1];
        while (true) {
            var count = await input.ReadAsync(character.AsMemory(), cancellation);
            if (count == 0) {
                return text.Length == 0 ? null : throw new InvalidDataException("Incomplete MCP message.");
            }

            if (character[0] == '\n') {
                if (text.Length > 0 && text[^1] == '\r')
                    text.Length--;
                return text.ToString();
            }

            if (text.Length >= limit)
                throw new InvalidDataException("MCP message exceeds the size limit.");
            _ = text.Append(character[0]);
        }
    }
}
