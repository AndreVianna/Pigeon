using System.Text.Json;
using Pigeon.Service;

// Hook input is untrusted host data. We read only the session ID, never transcripts or prompts.
internal static class SessionHook {
    public static async Task<string> ReadSessionIdAsync() {
        var input = new char[8193];
        var count = 0;
        while (count < input.Length) {
            var read = await Console.In.ReadAsync(input.AsMemory(count));
            if (read == 0) break;
            count += read;
        }
        if (count == input.Length) throw new ArgumentException("Hook input too large");
        using var json = JsonDocument.Parse(new string(input, 0, count));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("session_id", out var session) || session.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(session.GetString()))
            throw new ArgumentException("Missing hook session ID");
        return session.GetString()!;
    }

    // Rebinding also re-arms a wake that gave up, so a returning session receives what is waiting.
    public static async Task<int> RunAsync(string port, string tool) {
        try {
            var sessionId = await ReadSessionIdAsync();
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

    private static void Write(string message) => Console.WriteLine(JsonSerializer.Serialize(new { systemMessage = message }));
}
