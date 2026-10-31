using System.Text.Json;
using Pigeon.Adapters;
using Pigeon.Core;
using Xunit;

namespace Pigeon.UnitTests;

public sealed class CodexQueueNotifierTests {
    private const string Session = "8d77a0ec-d9c8-46b4-9948-7831ad4609ef";

    [Fact]
    public async Task Failed_queue_attempt_is_recorded_and_not_immediately_retried() {
        var calls = 0;
        var outcomes = new List<string>();
        var notifier = Create(Session, "codex", Session, (_, _, _) => { calls++; return Task.FromResult(false); }, outcomes);
        for (var i = 0; i < 4; i++) await notifier.TickAsync(CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.Equal(["transport-failed"], outcomes);
    }

    [Theory]
    [InlineData("not-a-uuid", "codex", "not-a-uuid")]
    [InlineData(Session, "claude-code", Session)]
    [InlineData(Session, "codex", "c1122145-145c-4af1-b02b-c2731e423fed")]
    public async Task Does_not_queue_for_an_invalid_or_mismatched_binding(string claimed, string runtime, string bound) {
        var calls = 0;
        var notifier = Create(claimed, runtime, bound, (_, _, _) => { calls++; return Task.FromResult(true); }, []);
        await notifier.TickAsync(CancellationToken.None);
        Assert.Equal(0, calls);
    }

    private static CodexQueueNotifier Create(string claimed, string runtime, string bound,
        Func<string, string, CancellationToken, Task<bool>> queue, List<string> outcomes) {
        var claimedOnce = false;
        return new(() => new Principal("test", "recipient"), () => claimed,
            (_, operation, args, _) => {
                object result = operation switch {
                    "connection" => new { Ok = true, Value = new { binding = new { Runtime = runtime, SessionId = bound } } },
                    "wake_claim" => ClaimOnce(ref claimedOnce),
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
