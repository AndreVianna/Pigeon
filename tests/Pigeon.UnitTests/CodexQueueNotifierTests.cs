using System.Text.Json;
using Pigeon.Adapters;
using Xunit;

namespace Pigeon.UnitTests;

public sealed class CodexQueueNotifierTests {
    private const string Session = "8d77a0ec-d9c8-46b4-9948-7831ad4609ef";

    [Fact]
    public async Task Failed_queue_attempt_is_recorded_and_not_immediately_retried() {
        var calls = 0;
        var outcomes = new List<string>();
        var notifier = Create(Session, claimAccepted: true, (_, _, _) => { calls++; return Task.FromResult(false); }, outcomes);
        for (var i = 0; i < 4; i++) await notifier.TickAsync(CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.Equal(["transport-failed"], outcomes);
    }

    [Fact]
    public async Task Queues_the_fixed_wake_prompt_for_the_bound_session_only() {
        string? target = null, prompt = null;
        var notifier = Create(Session, claimAccepted: true, (session, message, _) => { target = session; prompt = message; return Task.FromResult(true); }, []);
        await notifier.TickAsync(CancellationToken.None);
        Assert.Equal(Session, target);
        Assert.StartsWith("Pigeon wake generation 1284d10c6f3f471fa834a87ba13e71fa. Call pigeon_receive", prompt);
    }

    [Theory]
    [InlineData("not-a-uuid", true)]
    [InlineData(null, true)]
    // The service refuses the claim when the session did not join as codex.
    [InlineData(Session, false)]
    public async Task Does_not_queue_for_an_invalid_or_refused_session(string? bound, bool claimAccepted) {
        var calls = 0;
        var notifier = Create(bound, claimAccepted, (_, _, _) => { calls++; return Task.FromResult(true); }, []);
        await notifier.TickAsync(CancellationToken.None);
        Assert.Equal(0, calls);
    }

    private static CodexQueueNotifier Create(string? bound, bool claimAccepted,
        Func<string, string, CancellationToken, Task<bool>> queue, List<string> outcomes) {
        var claimedOnce = false;
        return new(() => bound,
            (operation, args, _) => {
                object result = operation switch {
                    "wake_claim" => claimAccepted ? ClaimOnce(ref claimedOnce) : new { Ok = false, Error = "Wake mode does not match the session's tool." },
                    "wake_result" => Recorded(args, outcomes),
                    _ => throw new InvalidOperationException()
                };
                return Task.FromResult(JsonSerializer.SerializeToElement(result));
            }, queue);
    }

    private static object ClaimOnce(ref bool claimed) {
        if (claimed) return new { Ok = true, Value = (object?)null };
        claimed = true;
        return new { Ok = true, Value = new { Generation = "1284d10c6f3f471fa834a87ba13e71fa", Attempt = 1 } };
    }

    private static object Recorded(object args, List<string> outcomes) {
        outcomes.Add(JsonSerializer.SerializeToElement(args).GetProperty("outcome").GetString()!);
        return new { Ok = true };
    }
}
