using Pigeon.Adapters;

using Xunit;

namespace Pigeon.UnitTests;

public class AdapterTests {
    private static IRuntimeAdapter Adapter(string name) => name switch { "codex" => new CodexAdapter(), "claude" => new ClaudeCodeAdapter(), "antigravity" => new AntigravityAdapter(), "opencode" => new OpenCodeAdapter(), "copilot" => new CopilotAdapter(), _ => throw new ArgumentException(nameof(name)) };

    [Fact]
    public void Only_the_five_expected_adapters_are_bundled() => Assert.Equal(["codex", "claude-code", "antigravity", "opencode", "copilot"], BundledAdapters.All().Select(a => a.Runtime));

    [Fact]
    public void Copilot_identifies_existing_sessions_from_the_host() {
        var capabilities = new CopilotAdapter().Capabilities;
        Assert.Equal(CapabilityState.Available, capabilities.IdentifyExistingSession.State);
        Assert.Equal(CapabilityState.Available, capabilities.SafeCheckpoint.State);
        Assert.Contains("idle", capabilities.SafeCheckpoint.Detail);
    }

    [Fact]
    public void OpenCode_identifies_existing_sessions_from_the_host() {
        var capabilities = new OpenCodeAdapter().Capabilities;
        Assert.Equal(CapabilityState.Available, capabilities.IdentifyExistingSession.State);
        Assert.Equal(CapabilityState.Available, capabilities.SafeCheckpoint.State);
        Assert.Contains("idle", capabilities.SafeCheckpoint.Detail);
    }

    [Fact]
    public void Antigravity_identifies_existing_sessions_from_the_host() {
        var capabilities = new AntigravityAdapter().Capabilities;
        Assert.Equal(CapabilityState.Available, capabilities.IdentifyExistingSession.State);
        Assert.Equal(CapabilityState.Available, capabilities.SafeCheckpoint.State);
        Assert.Contains("idle", capabilities.SafeCheckpoint.Detail);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void Adapter_distinguishes_demonstrated_capabilities_from_unimplemented_session_binding(string name) {
        var adapter = Adapter(name);
        var capabilities = adapter.Capabilities;
        Assert.All([capabilities.IdentifyExistingSession, capabilities.IdentifyNewSession],
            capability => Assert.Equal(CapabilityState.NotImplemented, capability.State));
        Assert.Equal(CapabilityState.Available, capabilities.SafeCheckpoint.State);
        Assert.Equal(CapabilityState.NotImplemented, capabilities.LiveNotification.State);
        Assert.Equal(CapabilityState.Available, capabilities.Polling.State);
        Assert.Contains("isolated", capabilities.Polling.Detail);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("antigravity")]
    [InlineData("opencode")]
    [InlineData("copilot")]
    public void Stub_does_not_claim_delivery_or_change_mode(string name) {
        var adapter = Adapter(name);
        foreach (var origin in new[] { SessionOrigin.Existing, SessionOrigin.NewlyLaunched }) {
            foreach (var mode in new[] { NotificationMode.Live, NotificationMode.SafeCheckpoint }) {
                var result = adapter.Notify(new RuntimeSession("project", "agent", "fixture", origin), new RuntimeMessage("id", "synthetic", "correlation"), mode);
                Assert.Equal(NotificationOutcome.Unavailable, result.Outcome);
                Assert.Contains("no delivery was attempted", result.Detail);
            }
        }
    }
}
