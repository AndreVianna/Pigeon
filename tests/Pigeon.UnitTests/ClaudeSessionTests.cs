using System.Text.Json;

using Pigeon.Service;

using Xunit;

namespace Pigeon.UnitTests;

// What the service tells a Claude session's hook waiter: whether the session's MCP server closed for
// good, and whether there is anything left to watch.
public sealed class ClaudeSessionTests : IDisposable {
    private const string Claude = "claude-session", Codex = "codex-session";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-claude-session-" + Guid.NewGuid().ToString("N"));
    private readonly Broker broker;
    private long now = 1_790_000_000_000;

    public ClaudeSessionTests() {
        Directory.CreateDirectory(directory);
        broker = new Broker(Path.Combine(directory, "messages.db"), () => now);
        _ = Ok("join", new { tool = "codex", sessionId = Codex, topic = "review" });
        _ = Ok("join", new { tool = "claude-code", sessionId = Claude, topic = "review" });
    }

    private JsonElement Ok(string operation, object args) {
        var result = broker.Execute(operation, JsonSerializer.SerializeToElement(args));
        Assert.True(result.Ok, result.Error);
        return JsonSerializer.SerializeToElement(result.Value);
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
    private void Server(string id) => Ok("bridge_heartbeat", new { id, mode = "mcp", sessionId = Claude });
    private bool Closed() => Ok("claude_session_closed", new { sessionId = Claude }).GetBoolean();

    [Fact]
    public void A_closed_server_is_reported_after_three_seconds_with_no_replacement() {
        var server = NewId();
        Server(server);
        Assert.False(Closed());
        _ = Ok("bridge_disconnect", new { id = server, sessionId = Claude });
        now += 2999;
        Assert.False(Closed());
        now += 1;
        Assert.True(Closed());
        // Past Claude Code's wait for its hooks, a waiter of the session opened again stays.
        now += Broker.ClosedReportMilliseconds;
        Assert.False(Closed());
    }

    [Fact]
    public void A_server_restarted_by_its_host_within_the_grace_keeps_the_session_open() {
        var server = NewId();
        Server(server);
        _ = Ok("bridge_disconnect", new { id = server, sessionId = Claude });
        now += 2000;
        Server(NewId());
        now += 10000;
        Assert.False(Closed());
        // A waiter's own bridge says nothing about the session's server.
        _ = Ok("bridge_disconnect", new { id = NewId() });
        now += 10000;
        Assert.False(Closed());
    }

    // Found in the 0.7.0-beta.3 test: a wake written as `claude -p` ended came back only after its
    // two-minute retry time, even when the session was opened again.
    [Fact]
    public void A_new_server_after_a_recorded_close_offers_the_sent_wake_again_and_a_plain_heartbeat_does_not() {
        var server = NewId();
        Server(server);
        _ = Ok("send", new { sessionId = Codex, topic = "review", message = "hello" });
        var holder = NewId();
        _ = Ok("wake_owner", new { sessionId = Claude, holderId = holder, mode = "claude-hook" });
        var claim = Ok("wake_claim", new { sessionId = Claude, mode = "claude-hook", holderId = holder });
        _ = Ok("wake_result", new { sessionId = Claude, generation = claim.GetProperty("Generation").GetString(), attempt = 1, outcome = "transport-written" });
        Server(server);
        Assert.Equal(JsonValueKind.Null, Ok("wake_claim", new { sessionId = Claude, mode = "claude-hook", holderId = holder }).ValueKind);

        _ = Ok("bridge_disconnect", new { id = server, sessionId = Claude });
        now += 60000;
        Server(NewId());
        // The reopened session's waiter.
        holder = NewId();
        _ = Ok("wake_owner", new { sessionId = Claude, holderId = holder, mode = "claude-hook" });
        var again = Ok("wake_claim", new { sessionId = Claude, mode = "claude-hook", holderId = holder });
        Assert.Equal(claim.GetProperty("Generation").GetString(), again.GetProperty("Generation").GetString());
        Assert.Equal(2, again.GetProperty("Attempt").GetInt32());
    }

    private JsonElement Owner(string holder) => Ok("wake_owner", new { sessionId = Claude, holderId = holder, mode = "claude-hook" });

    // Found in the 0.7.0-beta.3 test: after the agent left its only topic, `pigeon hook wait` kept running.
    [Fact]
    public void Ownership_says_there_is_nothing_to_watch_once_the_last_topic_is_left() {
        var holder = NewId();
        Assert.True(Owner(holder).GetProperty("watch").GetBoolean());
        _ = Ok("leave", new { sessionId = Claude, topic = "review" });
        var owner = Owner(holder);
        Assert.True(owner.GetProperty("owner").GetBoolean());
        Assert.False(owner.GetProperty("watch").GetBoolean());
    }

    // The first version of the fix stopped the waiter while the session waited for its own join,
    // so the session missed the outcome.
    [Fact]
    public void Ownership_keeps_watching_while_the_sessions_own_join_request_is_open() {
        _ = Ok("join", new { tool = "codex", sessionId = "opener", topic = "guarded", approval = true });
        _ = Ok("leave", new { sessionId = Claude, topic = "review" });
        var pending = Ok("join", new { tool = "claude-code", sessionId = Claude, topic = "guarded" });
        Assert.True(pending.GetProperty("pending").GetBoolean());
        var holder = NewId();
        Assert.True(Owner(holder).GetProperty("watch").GetBoolean());
        _ = Ok("request_deny", new { id = pending.GetProperty("request").GetString() });
        // The refusal is waiting for the session, so the waiter still has something to deliver.
        Assert.True(Owner(holder).GetProperty("watch").GetBoolean());
    }

    public void Dispose() {
        broker.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
