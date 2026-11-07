namespace Pigeon.Service;

public sealed record McpOutboundNotification(string Json, Func<CancellationToken, Task>? Written = null);

// The stdio bridge has no database path and never starts a private service.
public static class McpStdio {
    public static async Task RunAsync(TextReader input, TextWriter output, McpSession session, CancellationToken cancellation = default, Func<CancellationToken, Task<McpOutboundNotification?>>? nextNotification = null, string notificationMode = "codex-queue") {
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var writeGate = new SemaphoreSlim(1, 1);
        async Task WriteAsync(string line, CancellationToken token) {
            await writeGate.WaitAsync(token);
            try {
                await output.WriteLineAsync(line.AsMemory(), token);
                await output.FlushAsync(token);
            }
            finally {
                _ = writeGate.Release();
            }
        }

        async Task NotificationsAsync() {
            while (!stopped.IsCancellationRequested) {
                await Task.Delay(500, stopped.Token);
                if (!session.IsReady || nextNotification is null)
                    continue;
                McpOutboundNotification? notification;
                try {
                    notification = await nextNotification(stopped.Token);
                }
                catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException) {
                    continue;
                }
                catch (OperationCanceledException) when (!stopped.IsCancellationRequested) {
                    continue;
                }

                if (notification is null)
                    continue;
                await WriteAsync(notification.Json, stopped.Token);
                if (notification.Written is not null) {
                    try {
                        await notification.Written(stopped.Token);
                    }
                    catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException) { }
                    catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { }
                }
            }
        }

        var bridgeId = Guid.NewGuid().ToString("N");
        async Task HeartbeatAsync() {
            while (!stopped.IsCancellationRequested) {
                if (session.IsReady) {
                    try {
                        await session.TryRestoreAsync(stopped.Token);
                        var conversations = session.Conversations;
                        if (conversations.Count == 0)
                            _ = await session.BridgeAsync("bridge_heartbeat", new { id = bridgeId, mode = nextNotification is null ? "mcp" : notificationMode, sessionId = session.SessionId, restoration = session.RestoreState }, stopped.Token);
                        // An Antigravity connection reports each conversation it serves as connected.
                        foreach (var (id, conversation) in conversations)
                            _ = await session.BridgeAsync("bridge_heartbeat", new { id, mode = "mcp", sessionId = conversation, restoration = "bound" }, stopped.Token);
                    }
                    catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                    catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { }
                }

                await Task.Delay(3000, stopped.Token);
            }
        }

        // Join requests and codes are shown to the person by elicitation as soon as they arrive,
        // outside any tool call, so no model turn is spent and no tool-call timeout applies. A host
        // that cannot show them gets native dialogs instead, one window per request.
        var windows = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        async Task WindowAsync(string id, DialogRequest request, CancellationTokenSource closed) {
            DialogOutcome outcome;
            try {
                outcome = await session.DialogAsync(request, closed.Token);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) {
                outcome = DialogOutcome.Broken;
            }

            // The window is forgotten before the answer goes, so a wrong code is asked again.
            lock (windows)
                _ = windows.Remove(id);
            closed.Dispose();
            if (outcome.Failed)
                session.DialogsFailed();
            if (outcome.Action is null || stopped.IsCancellationRequested)
                return;
            try {
                _ = await session.BridgeAsync("prompt_answer", new { id, action = outcome.Action, code = outcome.Code }, stopped.Token);
            }
            catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException or OperationCanceledException) { }
        }

        async Task CloseEndedWindowsAsync() {
            string[] showing;
            lock (windows)
                showing = [.. windows.Keys];
            if (showing.Length == 0)
                return;
            var open = await session.BridgeAsync("prompts_open", new { ids = showing }, stopped.Token);
            if (!open.GetProperty("Ok").GetBoolean())
                return;
            var still = open.GetProperty("Value").EnumerateArray().Select(e => e.GetString()).ToHashSet(StringComparer.Ordinal);
            lock (windows) {
                foreach (var id in showing.Where(id => !still.Contains(id)))
                    if (windows.TryGetValue(id, out var window))
                        window.Cancel();
            }
        }

        async Task PromptsAsync() {
            while (!stopped.IsCancellationRequested) {
                await Task.Delay(1000, stopped.Token);
                if (!session.IsReady || !session.ShowsPrompts || session.ServedSessions() is not { Length: > 0 } sessions)
                    continue;
                try {
                    if (session.UsesDialogs)
                        await CloseEndedWindowsAsync();
                    var next = await session.BridgeAsync("prompts_next", new { bridgeId, sessions }, stopped.Token);
                    if (!next.GetProperty("Ok").GetBoolean())
                        continue;
                    foreach (var prompt in next.GetProperty("Value").EnumerateArray()) {
                        var id = prompt.GetProperty("id").GetString()!;
                        var message = prompt.GetProperty("message").GetString()!;
                        var codeField = prompt.GetProperty("code").GetBoolean();
                        if (!session.UsesDialogs) {
                            await WriteAsync(McpSession.PromptRequest(id, message, codeField), stopped.Token);
                            continue;
                        }

                        var show = prompt.TryGetProperty("show", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String ? s.GetString() : null;
                        var request = new DialogRequest(show is not null ? "show" : codeField ? "code" : "ask", message, show);
                        lock (windows) {
                            if (windows.ContainsKey(id))
                                continue;
                            var closed = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                            windows[id] = closed;
                            _ = WindowAsync(id, request, closed);
                        }
                    }
                }
                catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException) { }
                catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { }
            }
        }

        var heartbeat = HeartbeatAsync();
        var prompts = PromptsAsync();
        var notifications = nextNotification is null ? Task.CompletedTask : NotificationsAsync();
        try {
            while (!stopped.IsCancellationRequested) {
                var line = await ReadLineAsync(input, stopped.Token);
                if (line is null)
                    return;
                if (McpSession.PromptReply(line) is { } answer) {
                    try {
                        _ = await session.BridgeAsync("prompt_answer", new { id = answer.Id, action = answer.Action, code = answer.Code }, stopped.Token);
                    }
                    catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                    continue;
                }

                var reply = await session.HandleAsync(line, stopped.Token);
                if (reply is not null)
                    await WriteAsync(reply, stopped.Token);
            }
        }
        finally {
            stopped.Cancel();
            try {
                await notifications;
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }

            try {
                await prompts;
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }

            // Windows close with the connection; their requests stay open for the terminal.
            lock (windows) {
                foreach (var window in windows.Values)
                    window.Cancel();
            }

            try {
                await heartbeat;
            }
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
