using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;

namespace Pigeon.Adapters;

// A channel write is not confirmation; the recipient must confirm its wake.
public sealed class ClaudeChannel {
    public static object ExperimentalCapabilities => new Dictionary<string, object> { ["claude/channel"] = new { } };
    private readonly string port;
    private readonly Func<Principal> getPrincipal;
    private readonly Func<string?> getSessionId;
    private readonly string holderId = Guid.NewGuid().ToString("N");

    public ClaudeChannel(string port, Principal principal, string sessionId) {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A session claim is required.", nameof(sessionId));
        this.port = port;
        getPrincipal = () => principal;
        getSessionId = () => sessionId;
    }
    public ClaudeChannel(string port, Func<Principal> principal, Func<string?> sessionId) {
        this.port = port;
        getPrincipal = principal;
        getSessionId = sessionId;
    }

    public async Task<McpOutboundNotification?> NextAsync(CancellationToken cancellation) {
        var principal = getPrincipal();
        var sessionId = getSessionId();
        if (sessionId is null) return null;
        var connection = await LocalHttpClient.ExecuteAsync(port, principal, "connection", new { }, cancellation);
        if (!connection.GetProperty("Ok").GetBoolean()) return null;
        var binding = connection.GetProperty("Value").GetProperty("binding");
        if (binding.ValueKind == JsonValueKind.Null ||
            !string.Equals(binding.GetProperty("Runtime").GetString()?.Replace('_', '-'), "claude-code", StringComparison.OrdinalIgnoreCase) ||
            binding.GetProperty("SessionId").GetString() != sessionId) return null;
        // Another notifier, such as a monitor or hook waiter, may own this session's wakes.
        var ownership = await LocalHttpClient.ExecuteAsync(port, principal, "wake_owner", new { holderId, mode = "claude-channel" }, cancellation);
        if (!ownership.GetProperty("Ok").GetBoolean() || !ownership.GetProperty("Value").GetProperty("owner").GetBoolean()) return null;
        var claim = await LocalHttpClient.ExecuteAsync(port, principal, "wake_claim", new { mode = "claude-channel", holderId }, cancellation);
        if (!claim.GetProperty("Ok").GetBoolean() || claim.GetProperty("Value").ValueKind == JsonValueKind.Null) return null;
        var value = claim.GetProperty("Value");
        var generation = value.GetProperty("Generation").GetString()!;
        var attempt = value.GetProperty("Attempt").GetInt32();
        var json = JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "notifications/claude/channel", @params = new { content = WakePrompt.For(generation), meta = new { generation } } });
        return new McpOutboundNotification(json, async token => {
            _ = await LocalHttpClient.ExecuteAsync(port, principal, "wake_result", new { generation, attempt, outcome = "transport-written" }, token);
        });
    }
}
