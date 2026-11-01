namespace Pigeon.Adapters;

// A capability describes the shipped implementation, not what a vendor may document.
public enum CapabilityState { NotImplemented, Available, Unavailable }
public sealed record Capability(CapabilityState State, string Detail);
public sealed record AdapterCapabilities(
    Capability IdentifyExistingSession,
    Capability IdentifyNewSession,
    Capability LiveNotification,
    Capability SafeCheckpoint,
    Capability Polling);

public enum SessionOrigin { Existing, NewlyLaunched }
public sealed record RuntimeSession(string Project, string Agent, string? SessionId, SessionOrigin Origin);
public enum NotificationMode { Live, SafeCheckpoint }
public sealed record RuntimeMessage(string Id, string Body, string CorrelationId);
// TransportWritten and RuntimeAccepted do not imply that the agent read the message.
public enum NotificationOutcome { NotImplemented, Unavailable, TransportWritten, RuntimeAccepted }
public sealed record NotificationResult(NotificationOutcome Outcome, string Detail);

// Notification is separate from retrieval and acknowledgment. An adapter cannot
// silently change the requested mode or mark a message as acknowledged.
public interface IRuntimeAdapter {
    string Runtime { get; }
    AdapterCapabilities Capabilities { get; }
    NotificationResult Notify(RuntimeSession session, RuntimeMessage message, NotificationMode mode);
}

public sealed class CodexAdapter : IRuntimeAdapter {
    public string Runtime => "codex";
    public AdapterCapabilities Capabilities { get; } = new(
        new(CapabilityState.NotImplemented, "Existing-session binding and ownership are unverified."),
        new(CapabilityState.NotImplemented, "Pigeon does not launch Codex sessions."),
        new(CapabilityState.NotImplemented, "Live notification is unverified."),
        new(CapabilityState.Available, "An opt-in bridge queues a safe-checkpoint wake; actual runtime reachability depends on the session and host."),
        new(CapabilityState.Available, "Shared MCP polling was demonstrated in isolated sessions and a desktop chat."));

    public NotificationResult Notify(RuntimeSession session, RuntimeMessage message, NotificationMode mode)
        => new(NotificationOutcome.Unavailable, "Notifications are performed by the opted-in Codex queue bridge, not by this direct adapter method; no delivery was attempted.");
}

public sealed class ClaudeCodeAdapter : IRuntimeAdapter {
    public string Runtime => "claude-code";
    public AdapterCapabilities Capabilities { get; } = new(
        new(CapabilityState.NotImplemented, "A claimed session ID is not verified or automatically attached to a running session."),
        new(CapabilityState.NotImplemented, "Pigeon does not launch Claude Code sessions."),
        new(CapabilityState.NotImplemented, "Live notification is not implemented."),
        new(CapabilityState.Available, "The plugin's asynchronous Stop hook wakes an idle conversation whose process is running; a closed conversation cannot be woken."),
        new(CapabilityState.Available, "Shared MCP polling was demonstrated in a new isolated session; connection and existing-session availability are not verified."));

    public NotificationResult Notify(RuntimeSession session, RuntimeMessage message, NotificationMode mode)
        => new(NotificationOutcome.Unavailable, "Notifications are emitted by the plugin's hook waiter, not by this direct adapter method; no delivery was attempted.");
}

// Explicitly bundled adapters only: no dynamic loading or plugin discovery.
public static class BundledAdapters {
    public static IReadOnlyList<IRuntimeAdapter> All() => [new CodexAdapter(), new ClaudeCodeAdapter()];
}
