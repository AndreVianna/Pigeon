using Pigeon.Core;

using Xunit;

namespace Pigeon.UnitTests;

// Offering a sent wake again at once, for a Claude session whose host ended as the wake went out
// and whose new MCP server has now connected.
public sealed class WakeLifecycleTests : IDisposable {
    private const string Codex = "codex-session", Claude = "claude-session";
    private const long Retry = 120000;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-wake-lifecycle-" + Guid.NewGuid().ToString("N"));
    private readonly Mailbox store;
    private long now = 1_000_000;

    public WakeLifecycleTests() {
        Directory.CreateDirectory(directory);
        store = new Mailbox(Path.Combine(directory, "messages.db"), () => now, maxWakeAttempts: 3, wakeRetryMilliseconds: Retry);
        _ = store.Join("codex", Codex, "review");
        _ = store.Join("claude-code", Claude, "review");
        _ = store.Send(Codex, "review", "please review", "key-1", now + 3_600_000);
    }

    // A wake claimed and written to the host, which then ended before starting a turn.
    private WakeClaim Sent() {
        var claim = store.ClaimWake(Claude, "claude-hook")!;
        Assert.True(store.RecordWakeResult(Claude, claim.Generation, claim.Attempt, "transport-written"));
        Assert.Null(store.ClaimWake(Claude, "claude-hook"));
        return claim;
    }

    [Fact]
    public void A_reoffered_wake_is_due_at_once_with_the_same_generation() {
        var sent = Sent();
        Assert.True(store.ReofferWake(Claude));
        var again = store.ClaimWake(Claude, "claude-hook");
        Assert.NotNull(again);
        Assert.Equal(sent.Generation, again.Generation);
    }

    [Fact]
    public void Reoffering_keeps_the_attempt_count() {
        _ = Sent();
        Assert.True(store.ReofferWake(Claude));
        var status = store.GetWakeStatus(Claude);
        Assert.Equal(1, status.Attempts);
        Assert.Equal(now, status.NextAttemptUnixMs);
        Assert.Equal(2, store.ClaimWake(Claude, "claude-hook")!.Attempt);
    }

    [Fact]
    public void A_completed_generation_stays_stale_after_a_reoffer() {
        var sent = Sent();
        Assert.True(store.ReceiveForWake(Claude, sent.Generation).Accepted);
        // The agent is handling the wake: offering it again would only start a second turn.
        Assert.False(store.ReofferWake(Claude));
        Assert.True(store.FinishWake(Claude, sent.Generation));
        Assert.False(store.ReofferWake(Claude));
        Assert.False(store.ReceiveForWake(Claude, sent.Generation).Accepted);
        Assert.Equal("completed", store.GetWakeStatus(Claude).State);
    }

    [Fact]
    public void A_wake_that_gave_up_is_left_alone() {
        _ = Sent();
        for (var attempt = 0; attempt < 3; attempt++) {
            now += Retry;
            _ = store.ClaimWake(Claude, "claude-hook");
        }

        Assert.Equal("exhausted", store.GetWakeStatus(Claude).State);
        Assert.False(store.ReofferWake(Claude));
        Assert.Equal("exhausted", store.GetWakeStatus(Claude).State);
    }

    public void Dispose() {
        store.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
