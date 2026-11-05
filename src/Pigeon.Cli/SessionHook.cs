using System.Text.Json;

using Pigeon.Service;

// Hook input is untrusted host data. We read only the session ID, never transcripts or prompts.
internal static class SessionHook {
    // Antigravity sends camelCase fields and may include the user's prompt, so its input can be large.
    private static string IdField(string tool) => tool == "antigravity" ? "conversationId" : "session_id";

    public static async Task<string> ReadSessionIdAsync(string field = "session_id") {
        var input = new char[1048577];
        var count = 0;
        while (count < input.Length) {
            var read = await Console.In.ReadAsync(input.AsMemory(count));
            if (read == 0)
                break;
            count += read;
        }

        if (count == input.Length)
            throw new ArgumentException("Hook input too large");
        using var json = JsonDocument.Parse(input.AsMemory(0, count));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(field, out var session) || session.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(session.GetString()))
            throw new ArgumentException("Missing hook session ID");
        return session.GetString()!;
    }

    // Rebinding also re-arms a wake that gave up, so a returning session receives what is waiting.
    public static async Task<int> RunAsync(string port, string tool) {
        // Antigravity starts the first turn right after SessionStart; reporting it busy also restores it.
        if (tool == "antigravity")
            return await TurnAsync(port, tool, "busy", "{}");
        try {
            var sessionId = await ReadSessionIdAsync();
            if (!ServiceLock.MayBeHeld(port))
                throw new IOException("Pigeon service is not running");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await LocalHttpClient.ExecuteAsync(port, "session_restore", new { tool, sessionId }, timeout.Token);
            if (!result.GetProperty("Ok").GetBoolean())
                Write("Pigeon could not restore this session; run 'pigeon status' to check the service.");
            else if (result.GetProperty("Value").ValueKind == JsonValueKind.Object)
                Write("Pigeon restored this session's topics.");
            return 0;
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException or ArgumentException) {
            Write("Pigeon was unavailable; no service or topic was started.");
            return 0;
        }
    }

    // Reports a turn starting or ending, so a wake is sent only while the conversation is idle.
    // The reply is always the given fixed JSON: Antigravity's Stop hook requires a decision, and
    // a failure here must never keep the agent running or block its turn.
    public static async Task<int> TurnAsync(string port, string tool, string state, string reply) {
        try {
            var sessionId = await ReadSessionIdAsync(IdField(tool));
            if (!ServiceLock.MayBeHeld(port))
                throw new IOException("Pigeon service is not running");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            _ = await LocalHttpClient.ExecuteAsync(port, "turn_state", new { tool, sessionId, state }, timeout.Token);
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or JsonException or ArgumentException) { }

        Console.WriteLine(reply);
        return 0;
    }

    private static void Write(string message) => Console.WriteLine(JsonSerializer.Serialize(new { systemMessage = message }));
}
