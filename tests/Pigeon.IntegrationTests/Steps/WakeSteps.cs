using Pigeon.Core;

using Reqnroll;

using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "wake")]
public sealed class WakeSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-wake-" + Guid.NewGuid().ToString("N"));
    private long now = 1_000_000;
    private Mailbox store = null!;
    private int key;
    private const string Codex = "codex-session", Claude = "claude-session";
    private const long Retry = 1000, Lease = 5000;

    private Mailbox Open() => new(Path.Combine(directory, "messages.db"), () => now, maxWakeAttempts: 2, wakeRetryMilliseconds: Retry, leaseMilliseconds: Lease);
    private void Send(string text, string topic = "review", string from = Codex) => store.Send(from, topic, text, "key-" + ++key, now + 3_600_000);
    private WakeClaim Claim() => store.ClaimWake(Claude, "claude-hook") ?? throw new Xunit.Sdk.XunitException("No wake was claimed.");

    [Given("a conversation between a Codex and a Claude session")]
    public void Conversation() {
        Directory.CreateDirectory(directory);
        store = Open();
        _ = store.Join("codex", Codex, "review");
        _ = store.Join("claude-code", Claude, "review");
    }

    [When("the Codex session sends three messages")]
    public void Burst() {
        foreach (var text in new[] { "one", "two", "three" }) {
            Send(text);
            now++;
        }
    }

    [Then("one wake is claimed for the Claude session, a wrong generation receives nothing, and the right one receives all three in order")]
    public void OneWake() {
        var claim = Claim();
        Assert.Equal(1, claim.Attempt);
        Assert.Null(store.ClaimWake(Claude, "claude-hook"));
        Assert.False(store.ReceiveForWake(Claude, Guid.NewGuid().ToString("N")).Accepted);
        var batch = store.ReceiveForWake(Claude, claim.Generation);
        Assert.True(batch.Accepted);
        Assert.Equal(["one", "two", "three"], batch.Messages.Select(m => m.Body));
        Assert.Equal(0, batch.Remaining);
    }

    [Then("after receiving and completing the wake nothing is waiting")]
    public void Complete() {
        var claim = Claim();
        Assert.Equal(3, store.ReceiveForWake(Claude, claim.Generation).Messages.Length);
        Assert.Equal(3, store.GetWakeStatus(Claude).Leased);
        Assert.True(store.FinishWake(Claude, claim.Generation));
        var status = store.GetWakeStatus(Claude);
        Assert.Equal(("completed", 0, 0), (status.State, status.Queued, status.Leased));
        Assert.Empty(store.Take(Claude, "review"));
        Assert.True(store.FinishWake(Claude, claim.Generation));
    }

    [Then("a message sent while a wake is being handled is left for a new wake with a new generation")]
    public void DuringWake() {
        Send("first");
        var claim = Claim();
        Assert.Single(store.ReceiveForWake(Claude, claim.Generation).Messages);
        Send("second");
        Assert.True(store.FinishWake(Claude, claim.Generation));
        Assert.Equal(("ready", 1), (store.GetWakeStatus(Claude).State, store.GetWakeStatus(Claude).Queued));
        var next = Claim();
        Assert.NotEqual(claim.Generation, next.Generation);
        Assert.False(store.ReceiveForWake(Claude, claim.Generation).Accepted);
        Assert.Equal(["second"], store.ReceiveForWake(Claude, next.Generation).Messages.Select(m => m.Body));
    }

    [Then("messages received under an abandoned wake return to the queue and a new wake delivers them again")]
    public void Abandoned() {
        Send("lost once");
        var claim = Claim();
        Assert.Single(store.ReceiveForWake(Claude, claim.Generation).Messages);
        now += Lease + 1;
        var retry = Claim();
        Assert.NotEqual(claim.Generation, retry.Generation);
        Assert.False(store.FinishWake(Claude, claim.Generation));
        Assert.Equal(["lost once"], store.ReceiveForWake(Claude, retry.Generation).Messages.Select(m => m.Body));
        Assert.True(store.FinishWake(Claude, retry.Generation));
        Assert.Equal("completed", store.GetWakeStatus(Claude).State);
    }

    [Then("a claimed wake keeps its attempt count and retry time after the store is reopened")]
    public void Restart() {
        Send("before restart");
        var claim = Claim();
        var next = store.GetWakeStatus(Claude).NextAttemptUnixMs;
        store.Dispose();
        store = Open();
        var status = store.GetWakeStatus(Claude);
        Assert.Equal((claim.Generation, 1, next), (status.Generation, status.Attempts, status.NextAttemptUnixMs));
        Assert.Null(store.ClaimWake(Claude, "claude-hook"));
        now += Retry;
        Assert.Equal(2, Claim().Attempt);
    }

    [Then("repeated unanswered wakes stop, a new message re-arms them, and so does restoring the session")]
    public void Exhaustion() {
        Send("unanswered");
        void Exhaust() {
            _ = Claim();
            now += Retry;
            _ = Claim();
            now += Retry;
            Assert.Null(store.ClaimWake(Claude, "claude-hook"));
            Assert.Equal("exhausted", store.GetWakeStatus(Claude).State);
        }

        Exhaust();
        Send("another");
        Assert.Equal(1, Claim().Attempt);
        now += Retry;
        _ = Claim();
        now += Retry;
        Assert.Null(store.ClaimWake(Claude, "claude-hook"));
        Assert.NotNull(store.Restore("claude-code", Claude));
        Assert.Equal(("ready", 0), (store.GetWakeStatus(Claude).State, store.GetWakeStatus(Claude).Attempts));
        Assert.Equal(1, Claim().Attempt);
    }

    [Then("a Codex queue wake cannot be claimed for the Claude session, nor any wake for an unknown session")]
    public void Modes() {
        Send("for claude");
        Assert.Throws<InvalidOperationException>(() => store.ClaimWake(Claude, "codex-queue"));
        Assert.Throws<InvalidOperationException>(() => store.ClaimWake("unknown", "claude-hook"));
        Assert.Throws<ArgumentException>(() => store.ClaimWake(Claude, "claude-monitor"));
        Assert.Equal(0, store.GetWakeStatus(Claude).Attempts);
    }

    [Then("a failed transport result recorded after the wake was received leaves it confirmed")]
    public void LateResult() {
        Send("late");
        var claim = Claim();
        Assert.True(store.ReceiveForWake(Claude, claim.Generation).Accepted);
        Assert.True(store.RecordWakeResult(Claude, claim.Generation, claim.Attempt, "transport-failed"));
        Assert.Equal("confirmed", store.GetWakeStatus(Claude).State);
        Assert.False(store.RecordWakeResult(Claude, claim.Generation, claim.Attempt, "transport-written"));
    }

    [Then("a claimed wake with no transport result is reported as failed with an unknown transport after its retry time")]
    public void Unknown() {
        Send("silent");
        _ = Claim();
        Assert.Equal("dispatching", store.GetWakeStatus(Claude).State);
        now += Retry;
        var status = store.GetWakeStatus(Claude);
        Assert.Equal(("failed", "transport-unknown"), (status.State, status.LastOutcome));
        Assert.Equal("transport-unknown", status.History[0].Outcome);
    }

    [Then("a Claude session in two topics receives both messages under one wake, each naming its topic")]
    public void TwoTopics() {
        _ = store.Join("codex", "other-codex", "tests");
        _ = store.Join("claude-code", Claude, "tests");
        Send("review note");
        now++;
        Send("test note", "tests", "other-codex");
        var claim = Claim();
        var batch = store.ReceiveForWake(Claude, claim.Generation);
        Assert.Equal([("review", "review note"), ("tests", "test note")], batch.Messages.Select(m => (m.Topic, m.Body)));
        Assert.Equal(1, store.GetWakeStatus(Claude).Attempts);
    }

    // Found in the 0.7.0-beta.3 test: a wake written as `claude -p` ended came back only after two
    // minutes, even when the session was opened again. Runs through the service, on its own store.
    [Then("a wake written as the session's MCP server closed is offered again at once to its next server, and a plain service restart does not offer it early")]
    public void OfferedAgain() {
        var path = Path.Combine(directory, "service.db");
        var broker = new Pigeon.Service.Broker(path, () => now, wakeRetryMilliseconds: 120000);
        System.Text.Json.JsonElement Ok(string operation, object args) {
            var result = broker.Execute(operation, System.Text.Json.JsonSerializer.SerializeToElement(args));
            Assert.True(result.Ok, result.Error);
            return System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        }

        string NewId() => Guid.NewGuid().ToString("N");
        System.Text.Json.JsonElement Claim(string holder) {
            _ = Ok("wake_owner", new { sessionId = Claude, holderId = holder, mode = "claude-hook" });
            return Ok("wake_claim", new { sessionId = Claude, mode = "claude-hook", holderId = holder });
        }

        try {
            _ = Ok("join", new { tool = "codex", sessionId = Codex, topic = "review" });
            _ = Ok("join", new { tool = "claude-code", sessionId = Claude, topic = "review" });
            var server = NewId();
            _ = Ok("bridge_heartbeat", new { id = server, mode = "mcp", sessionId = Claude });
            _ = Ok("send", new { sessionId = Codex, topic = "review", message = "as the run ends" });
            var sent = Claim(NewId());
            var generation = sent.GetProperty("Generation").GetString();
            _ = Ok("wake_result", new { sessionId = Claude, generation, attempt = 1, outcome = "transport-written" });
            _ = Ok("bridge_disconnect", new { id = server, sessionId = Claude });
            now += 20000;
            _ = Ok("bridge_heartbeat", new { id = NewId(), mode = "mcp", sessionId = Claude });
            var again = Claim(NewId());
            Assert.Equal((generation, 2), (again.GetProperty("Generation").GetString(), again.GetProperty("Attempt").GetInt32()));
            _ = Ok("wake_result", new { sessionId = Claude, generation, attempt = 2, outcome = "transport-written" });

            // A restarted service knows no close, so its servers' heartbeats change nothing.
            broker.Dispose();
            broker = new Pigeon.Service.Broker(path, () => now, wakeRetryMilliseconds: 120000);
            _ = Ok("bridge_heartbeat", new { id = NewId(), mode = "mcp", sessionId = Claude });
            Assert.Equal(System.Text.Json.JsonValueKind.Null, Claim(NewId()).ValueKind);
            Assert.Equal(2, Ok("wake_status", new { sessionId = Claude }).GetProperty("Attempts").GetInt32());
        }
        finally {
            broker.Dispose();
        }
    }

    [AfterScenario]
    public void Cleanup() {
        store?.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
