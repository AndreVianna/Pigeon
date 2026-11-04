using System.Text.Json;

using Pigeon.Adapters;

using Xunit;

namespace Pigeon.UnitTests;

public sealed class AntigravityWakeSidecarTests {
    private const string Conversation = "3f0d8a51-6c2e-4d8b-9a77-0b1c2d3e4f50", Generation = "1284d10c6f3f471fa834a87ba13e71fa";

    [Fact]
    public async Task Sends_the_fixed_wake_prompt_to_a_claimed_Antigravity_session_only() {
        var sends = new List<(string Session, string Message)>();
        var outcomes = new List<string>();
        var claims = new List<string>();
        var sidecar = Create(claimable: true, (session, message, _) => {
            sends.Add((session, message));
            return Task.FromResult(true);
        }, outcomes, claims);
        Assert.Equal(1, await sidecar.TickAsync(CancellationToken.None));
        var (session, message) = Assert.Single(sends);
        Assert.Equal(Conversation, session);
        Assert.StartsWith($"Pigeon wake generation {Generation}. Call pigeon_receive", message);
        Assert.Equal(["transport-accepted"], outcomes);
        // Sessions of other tools are not claimed.
        Assert.Equal([Conversation], claims);
    }

    [Fact]
    public async Task A_failed_send_is_recorded() {
        var outcomes = new List<string>();
        var sidecar = Create(claimable: true, (_, _, _) => Task.FromResult(false), outcomes, []);
        _ = await sidecar.TickAsync(CancellationToken.None);
        Assert.Equal(["transport-failed"], outcomes);
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_service_refuses_the_claim() {
        var calls = 0;
        var sidecar = Create(claimable: false, (_, _, _) => {
            calls++;
            return Task.FromResult(true);
        }, [], []);
        Assert.Equal(0, await sidecar.TickAsync(CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Run_reports_the_sidecar_before_its_first_tick() {
        var operations = new List<string>();
        using var stop = new CancellationTokenSource();
        var sidecar = new AntigravityWakeSidecar((operation, args, _) => {
            operations.Add(operation);
            if (operation == "bridge_heartbeat")
                Assert.Equal(AntigravityWakeSidecar.Mode, JsonSerializer.SerializeToElement(args).GetProperty("mode").GetString());
            if (operation == "wake_states")
                stop.Cancel();
            return Task.FromResult(JsonSerializer.SerializeToElement(new { Ok = true, Value = Array.Empty<object>() }));
        }, (_, _, _) => Task.FromResult(true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sidecar.RunAsync(stop.Token));
        Assert.Equal(["bridge_heartbeat", "wake_states"], operations);
    }

    private static AntigravityWakeSidecar Create(bool claimable, Func<string, string, CancellationToken, Task<bool>> send, List<string> outcomes, List<string> claims)
        => new((operation, args, _) => {
            var a = JsonSerializer.SerializeToElement(args);
            object result = operation switch {
                "wake_states" => new { Ok = true, Value = new[] { new { Tool = "codex", SessionId = "codex-session" }, new { Tool = "antigravity", SessionId = Conversation } } },
                "wake_claim" => Claim(a, claimable, claims),
                "wake_result" => Recorded(a, outcomes),
                _ => throw new InvalidOperationException()
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }, send);

    private static object Claim(JsonElement args, bool claimable, List<string> claims) {
        Assert.Equal(AntigravityWakeSidecar.Mode, args.GetProperty("mode").GetString());
        claims.Add(args.GetProperty("sessionId").GetString()!);
        return claimable ? new { Ok = true, Value = (object?)new { Generation, Attempt = 1 } } : new { Ok = true, Value = (object?)null };
    }

    private static object Recorded(JsonElement args, List<string> outcomes) {
        outcomes.Add(args.GetProperty("outcome").GetString()!);
        return new { Ok = true };
    }
}
