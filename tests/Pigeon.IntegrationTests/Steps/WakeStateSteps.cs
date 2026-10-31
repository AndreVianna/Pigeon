using Pigeon.Core;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "wake_state")]
public sealed class WakeStateSteps {
    private readonly string dir = Path.Combine(Path.GetTempPath(), "pigeon-wake-state-" + Guid.NewGuid().ToString("N"));
    private readonly Principal sender = new("wake-project", "sender");
    private readonly Principal recipient = new("wake-project", "recipient");
    private Mailbox box = null!;
    private long now = 1000;
    private int key;
    private WakeClaim claim = null!;
    private string Database => Path.Combine(dir, "wake.db");
    private Mailbox Open() => new(Database, () => now, maxWakeAttempts: 3, wakeRetryMilliseconds: 100);

    [Given("a durable Codex inbox with a pending message")]
    public void Setup() {
        Directory.CreateDirectory(dir);
        box = Open();
        box.Register(sender.Project, sender.Agent, "mock", "sender-session");
        box.Register(recipient.Project, recipient.Agent, "codex", "recipient-session");
        box.CreateTopic(sender, "topic");
        box.Subscribe(recipient, "topic");
        Publish();
    }
    private void Publish() => box.Publish(sender, "topic", "synthetic body", "conversation", "key-" + ++key, now + 1000000);

    [Then("a Claude monitor must match both the runtime and current session binding")]
    public void MonitorBinding() {
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(recipient, "claude-monitor", "recipient-session"));
        var claude = new Principal(recipient.Project, "claude");
        box.Register(claude.Project, claude.Agent, "claude-code", "new-session");
        box.Subscribe(claude, "topic");
        Publish();
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(claude, "claude-monitor", "recipient-session"));
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(recipient, "claude-hook", "recipient-session"));
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(claude, "claude-hook", "recipient-session"));
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(claude, "claude-hook"));
        Assert.Equal(0, box.GetWakeStatus(claude).Attempts);
        Assert.NotNull(box.ClaimWake(claude, "claude-monitor", "new-session"));
    }

    [When("a wake is claimed and another message arrives")]
    public void ClaimThenPublish() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Publish();
    }

    [Then("only one wake is outstanding until the agent confirms and drains the inbox")]
    public void Coalesced() {
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        Assert.True(box.RecordWakeResult(recipient, claim.Generation, claim.Attempt, "transport-accepted"));
        var confirmation = box.ConfirmWake(recipient, claim.Generation);
        Assert.True(confirmation.Accepted);
        Assert.Equal(2, confirmation.Queued);
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
        for (var i = 0; i < 2; i++) Assert.True(box.Ack(recipient, box.Receive(recipient)!.Receipt));
        Assert.True(box.FinishWake(recipient, claim.Generation));
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.Equal("completed", box.GetWakeStatus(recipient).State);
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
    }

    [When("a wake transport fails and the service restarts")]
    public void FailAndRestart() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.True(box.RecordWakeResult(recipient, claim.Generation, claim.Attempt, "transport-failed"));
        box.Dispose();
        box = Open();
    }
    [Then("retries remain bounded and explicit retry starts a new generation")]
    public void RestartRetry() {
        Assert.Equal("failed", box.GetWakeStatus(recipient).State);
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        now += 100;
        var second = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal(claim.Generation, second.Generation);
        Assert.Equal(2, second.Attempt);
        Assert.True(box.RecordWakeResult(recipient, second.Generation, second.Attempt, "transport-accepted"));
        now += 100;
        var third = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal(3, third.Attempt);
        now += 100;
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        var exhausted = box.GetWakeStatus(recipient);
        Assert.Equal("exhausted", exhausted.State);
        Assert.Contains(exhausted.History, attempt => attempt.Number == 3 && attempt.Outcome == "transport-unknown");
        Assert.Equal(1, box.Inbox(recipient).Queued);
        Assert.True(box.RetryWake(recipient));
        var retry = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.NotEqual(claim.Generation, retry.Generation);
        Assert.Equal(1, retry.Attempt);
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
    }

    [When("the agent confirms a wake but abandons a leased message")]
    public void AbandonLease() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.True(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.NotNull(box.Receive(recipient, leaseMilliseconds: 50));
        Assert.True(box.FinishWake(recipient, claim.Generation));
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        box.Dispose();
        box = Open();
        now += 100;
    }
    [Then("the next wake has a new generation and the old wake is stale")]
    public void LeaseRecovery() {
        var next = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.NotEqual(claim.Generation, next.Generation);
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.True(box.ConfirmWake(recipient, next.Generation).Accepted);
        var delivery = Assert.IsType<Envelope>(box.Receive(recipient));
        Assert.Equal(2, delivery.Attempt);
        Assert.True(box.Ack(recipient, delivery.Receipt));
    }

    [When("the agent confirms a wake before the transport result arrives")]
    public void ConfirmBeforeResult() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.True(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.True(box.RecordWakeResult(recipient, claim.Generation, claim.Attempt, "transport-accepted"));
    }
    [Then("the wake remains confirmed and the message remains unacknowledged")]
    public void ConfirmedNotAcknowledged() {
        Assert.Equal("confirmed", box.GetWakeStatus(recipient).State);
        Assert.Equal(1, box.Inbox(recipient).Queued);
        Assert.Equal(0, box.Inbox(recipient).Acknowledged);
    }

    [Then("paused, expired and wrong-project wake requests cannot consume that inbox")]
    public void IsolationAndExpiry() {
        box.Pause(recipient, true);
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        box.Pause(recipient, false);
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        var other = new Principal("other-project", "recipient");
        box.Register(other.Project, other.Agent, "codex", "different-session");
        Assert.False(box.ConfirmWake(other, claim.Generation).Accepted);
        Assert.Null(box.ClaimWake(other, "codex-queue"));
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(recipient, "claude-channel"));
        now += 1000001;
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal("completed", box.GetWakeStatus(recipient).State);
        Assert.Equal(1, box.Inbox(recipient).Expired);
    }

    [Then("concurrent claimants obtain only one wake attempt")]
    public async Task ConcurrentClaims() {
        using var other = Open();
        var claims = await Task.WhenAll(Task.Run(() => box.ClaimWake(recipient, "codex-queue")), Task.Run(() => other.ClaimWake(recipient, "codex-queue")));
        Assert.Single(claims, x => x is not null);
    }

    [When("a bridge crashes after claiming a wake")]
    public void CrashAfterClaim() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        box.Dispose();
        box = Open();
        now += 100;
    }
    [Then("the retry records the earlier transport outcome as unknown")]
    public void InterruptedTransport() {
        var retry = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal(2, retry.Attempt);
        var history = box.GetWakeStatus(recipient).History;
        Assert.Contains(history, a => a.Number == 1 && a.Outcome == "transport-unknown");
        Assert.False(box.RecordWakeResult(recipient, claim.Generation, claim.Attempt, "transport-accepted"));
    }

    [When("the first wake is confirmed and its message acknowledged")]
    public void FinishFirst() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.True(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.True(box.Ack(recipient, box.Receive(recipient)!.Receipt));
    }
    [Then("a later message receives a new generation that the old wake cannot confirm")]
    public void NewGeneration() {
        Publish();
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.False(box.FinishWake(recipient, claim.Generation));
        var next = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.NotEqual(claim.Generation, next.Generation);
        Assert.True(box.ConfirmWake(recipient, next.Generation).Accepted);
    }

    [Then("1025 successive wake generations can be confirmed and drained")]
    public void MoreThanPriorMemoryLimit() {
        for (var i = 0; i < 1025; i++) {
            if (i > 0) Publish();
            var wake = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
            Assert.True(box.ConfirmWake(recipient, wake.Generation).Accepted);
            Assert.True(box.Ack(recipient, box.Receive(recipient)!.Receipt));
            Assert.True(box.FinishWake(recipient, wake.Generation));
        }
        Assert.Equal("completed", box.GetWakeStatus(recipient).State);
        Assert.Equal(1025, box.Inbox(recipient).Acknowledged);
    }

    [Then("the offline recipient's ready wake remains visible after a service restart")]
    public void OfflineWakeSurvives() {
        var before = Assert.Single(box.PendingWakeStatuses(recipient.Project));
        Assert.Equal("recipient", before.Agent);
        Assert.Equal("ready", before.Wake.State);
        box.Dispose();
        box = Open();
        var after = Assert.Single(box.PendingWakeStatuses(recipient.Project));
        Assert.Equal(before.Wake.Generation, after.Wake.Generation);
        Assert.Equal(1, after.Wake.Queued);
    }

    [Then("finishing a partial batch re-arms work that arrived while processing")]
    public void ArrivalDuringBatch() {
        Publish();
        var wake = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.True(box.ConfirmWake(recipient, wake.Generation).Accepted);
        Assert.True(box.Ack(recipient, box.Receive(recipient)!.Receipt));
        Publish();
        Assert.True(box.FinishWake(recipient, wake.Generation));
        var next = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.NotEqual(wake.Generation, next.Generation);
        Assert.False(box.ConfirmWake(recipient, wake.Generation).Accepted);
        Assert.True(box.ConfirmWake(recipient, next.Generation).Accepted);
        Assert.Equal(2, box.Inbox(recipient).Queued);
    }

    [When("the wake tables are absent as in the previous version")]
    public void PreviousSchema() {
        box.Dispose();
        using var db = new Sqlite(Database);
        _ = db.Exec("DROP TABLE wake_attempts");
        _ = db.Exec("DROP TABLE wake_state");
    }
    [Then("the next service startup preserves the delivery and creates its wake lazily")]
    public void UpgradePendingDelivery() {
        box = Open();
        Assert.Equal(1, box.Inbox(recipient).Queued);
        Assert.Equal("ready", box.GetWakeStatus(recipient).State);
        Assert.NotNull(box.ClaimWake(recipient, "codex-queue"));
    }

    [Then("repeated confirmation without acknowledgement cannot cause an unbounded wake loop")]
    public void ConfirmationWithoutProgress() {
        for (var i = 0; i < 3; i++) {
            var wake = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
            Assert.True(box.ConfirmWake(recipient, wake.Generation).Accepted);
            Assert.True(box.FinishWake(recipient, wake.Generation));
        }
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal("exhausted", box.GetWakeStatus(recipient).State);
        Assert.Equal(1, box.Inbox(recipient).Queued);
    }

    [When("the wake budget is exhausted without progress")]
    public void Exhaust() {
        claim = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        for (var attempt = 2; attempt <= 3; attempt++) {
            now += 100;
            Assert.Equal(attempt, Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue")).Attempt);
        }
        now += 100;
        Assert.Null(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal("exhausted", box.GetWakeStatus(recipient).State);
    }
    [Then("a new message starts one fresh wake generation that a burst shares")]
    public void Rearmed() {
        Publish();
        var rearmed = box.GetWakeStatus(recipient);
        Assert.Equal("ready", rearmed.State);
        Assert.Equal(0, rearmed.Attempts);
        Assert.NotEqual(claim.Generation, rearmed.Generation);
        Publish();
        Assert.Equal(rearmed.Generation, box.GetWakeStatus(recipient).Generation);
        var next = Assert.IsType<WakeClaim>(box.ClaimWake(recipient, "codex-queue"));
        Assert.Equal(rearmed.Generation, next.Generation);
        Assert.Equal(1, next.Attempt);
        Assert.False(box.ConfirmWake(recipient, claim.Generation).Accepted);
        Assert.Equal(3, box.Inbox(recipient).Queued);
    }

    [Then("an identity registered with the runtime \"Claude Code\" can claim hook and channel wakes")]
    public void LegacyClaudeRuntime() {
        var legacy = new Principal(recipient.Project, "legacy-claude");
        box.Register(legacy.Project, legacy.Agent, "Claude Code", "legacy-session");
        box.Subscribe(legacy, "topic");
        Publish();
        Assert.NotNull(box.ClaimWake(legacy, "claude-hook", "legacy-session"));
        now += 100;
        Assert.NotNull(box.ClaimWake(legacy, "claude-channel"));
        Assert.Throws<InvalidOperationException>(() => box.ClaimWake(legacy, "codex-queue"));
    }

    [Then("a wake claim during another writer's lock fails as storage unavailable and later succeeds")]
    public void HeldLock() {
        using (var other = new Sqlite(Database)) {
            _ = other.Exec("BEGIN IMMEDIATE");
            Assert.Throws<StorageUnavailableException>(() => box.ClaimWake(recipient, "codex-queue"));
            _ = other.Exec("ROLLBACK");
        }
        Assert.NotNull(box.ClaimWake(recipient, "codex-queue"));
    }

    [AfterScenario]
    public void Cleanup() {
        box?.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
