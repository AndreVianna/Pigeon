using System.Text.Json;
using Pigeon.Core;

namespace Pigeon.Service;

public sealed record BrokerResult(bool Ok, object? Value, string? Error);

public sealed class Broker : IDisposable {
    private readonly Mailbox mailbox;
    private readonly long? bodyRetentionMilliseconds;
    private readonly Func<long> clock;
    private readonly object gate = new();
    private readonly Dictionary<string, (string Project, string Agent, string Mode, string? Restoration, long Seen)> bridges = new(StringComparer.Ordinal);
    // One Claude notifier owns an identity's wakes at a time. Ownership is a routing
    // safeguard kept only in memory; holders renew it every tick and reacquire after a restart.
    private readonly Dictionary<(string Project, string Agent), (string Holder, string Mode, long Seen)> owners = [];
    private readonly Dictionary<string, long> superseded = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Project, string Agent), (string Mode, string Reason, long At)> released = [];
    private readonly Dictionary<string, long> lastRedaction = new(StringComparer.Ordinal);
    private const long OwnerTimeoutMilliseconds = 15000;
    // Clients treat this error as a temporary service outage, not a rejected request.
    public const string StorageUnavailable = "storage unavailable";

    public Broker(string databasePath, Func<long>? clock = null, long? bodyRetentionMilliseconds = null, int maxAttempts = 3, int maxAutomated = 4, int maxAutomatedPerMinute = 100, bool topicMode = false, int maxWakeAttempts = 3, long wakeRetryMilliseconds = 120000) {
        if (bodyRetentionMilliseconds is <= 0)
            throw new ArgumentOutOfRangeException(nameof(bodyRetentionMilliseconds));
        this.bodyRetentionMilliseconds = bodyRetentionMilliseconds;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        mailbox = new Mailbox(databasePath, clock, maxAttempts, maxAutomated, maxAutomatedPerMinute, maxWakeAttempts, wakeRetryMilliseconds);
        if (topicMode) {
            try { mailbox.EnableTopicMode(); }
            catch { mailbox.Dispose(); throw; }
        }
    }

    // Identity and project are caller-supplied routing labels, not authentication.
    internal BrokerResult Execute(Principal principal, string operation, JsonElement args) {
        lock (gate) {
            try {
                if (args.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Arguments must be an object");
                // Perform optional maintenance before the requested operation so
                // a maintenance error cannot disguise a committed send or ack.
                // The sweep scans every terminal message in a write transaction, so it runs at most
                // once per interval per project rather than on every heartbeat and waiter poll.
                if (bodyRetentionMilliseconds is long retention && clock() - lastRedaction.GetValueOrDefault(principal.Project, long.MinValue / 2) >= Math.Min(retention, 60000) && mailbox.FindIdentity(principal) is not null) {
                    _ = mailbox.RedactTerminalBodies(principal, retention);
                    lastRedaction[principal.Project] = clock();
                }
                // Agent-originated sends are always classified as automated. The
                // host, not caller input, chooses this safety classification.
                var result = operation switch {
                    "register" => Register(principal, args),
                    "session_join" => mailbox.JoinSession(principal.Project, Str(args, "tool"), Str(args, "sessionId"), Str(args, "topic")),
                    "session_restore" => mailbox.RestoreSession(principal.Project, Str(args, "tool"), Str(args, "sessionId")),
                    "session_candidates" => mailbox.LegacyCandidates(principal.Project, Str(args, "tool"), Str(args, "sessionId")),
                    "session_adopt" => mailbox.AdoptLegacySession(principal.Project, Str(args, "tool"), Str(args, "sessionId"), Str(args, "legacyAgent")),
                    "session_send" => SessionSend(principal, args),
                    "session_leave" => SessionLeave(principal, args),
                    "topic_sessions" => mailbox.TopicSessions(principal.Project, Str(args, "topic")),
                    "connection" => new { service = "available", project = principal.Project, agent = principal.Agent, binding = mailbox.FindIdentity(principal) },
                    "bridge_heartbeat" => BridgeHeartbeat(principal, args),
                    "bridge_disconnect" => BridgeDisconnect(principal, args),
                    "bridges" => ActiveBridges(principal),
                    "claude_session_bridges" => ClaudeSessionBridges(principal, Str(args, "sessionId")),
                    "identity" => mailbox.Identity(principal),
                    "inbox_status" => mailbox.Inbox(principal),
                    "send" => Send(principal, args),
                    "receive" => mailbox.Receive(principal, 30000, Optional(args, "topic")),
                    "wake_owner" => WakeOwner(principal, args),
                    "wake_owner_check" => WakeOwnerCheck(principal, args),
                    "wake_owner_release" => WakeOwnerRelease(principal, args),
                    "wake_claim" => ClaimWake(principal, args),
                    "wake_result" => mailbox.RecordWakeResult(principal, Str(args, "generation"), checked((int)(OptionalNumber(args, "attempt") ?? throw new ArgumentException())), Str(args, "outcome")),
                    "wake_confirm" => mailbox.ConfirmWake(principal, Str(args, "generation")),
                    "wake_complete" => mailbox.FinishWake(principal, Str(args, "generation")),
                    "wake_status" => mailbox.GetWakeStatus(principal),
                    "wake_retry" => mailbox.RetryWake(principal),
                    "wake_states" => mailbox.PendingWakeStatuses(principal.Project),
                    "acknowledge" => mailbox.Ack(principal, Str(args, "receipt")),
                    "subscribe" => Subscribe(principal, args),
                    "topic_join" => JoinTopic(principal, args),
                    "topic_create" => CreateTopic(principal, args),
                    "topic_delete" => mailbox.DeleteTopicForProject(principal.Project, Str(args, "topic")),
                    "topics" => mailbox.TopicsForProject(principal.Project),
                    "topic_read" => mailbox.ReadTopic(principal, Str(args, "topic"), OptionalNumber(args, "afterSequence"), checked((int)(OptionalNumber(args, "limit") ?? 3))),
                    "mailboxes" => mailbox.Subscriptions(principal),
                    "publish" => Publish(principal, args),
                    "status" => mailbox.Details(principal, Str(args, "messageId")),
                    "pause" => Pause(principal, args),
                    "activity" => mailbox.Activity(principal, OptionalNumber(args, "beforeSequence"), checked((int)(OptionalNumber(args, "limit") ?? 10))),
                    _ => throw new ArgumentException("Unknown operation")
                };
                return new(true, result, null);
            }
            catch (ArgumentException) {
                return new(false, null, "invalid request");
            }
            catch (InvalidOperationException e) {
                return new(false, null, e.Message == "Unknown topic" ? "Topic does not exist" : e.Message.StartsWith("Ambiguous legacy tool and session", StringComparison.Ordinal) ? e.Message : "operation rejected");
            }
            catch (OverflowException) {
                return new(false, null, "invalid request");
            }
            catch (StorageUnavailableException) {
                return new(false, null, StorageUnavailable);
            }
        }
    }

    private object BridgeHeartbeat(Principal p, JsonElement args) {
        var id = Str(args, "id");
        var mode = Str(args, "mode");
        var restoration = Optional(args, "restoration");
        if (!Guid.TryParseExact(id, "N", out _) || mode is not ("mcp" or "claude-channel" or "codex-queue" or "claude-monitor" or "claude-hook"))
            throw new ArgumentException();
        if (restoration is not (null or "pending" or "bound" or "missing-session" or "not-configured" or "conflict")) throw new ArgumentException();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var stale in bridges.Where(pair => pair.Value.Seen < now - 15000).Select(pair => pair.Key).ToArray()) bridges.Remove(stale);
        if (!bridges.ContainsKey(id) && bridges.Count >= 1024) throw new InvalidOperationException();
        bridges[id] = (p.Project, p.Agent, mode, restoration, now);
        return new { recorded = true };
    }
    private object BridgeDisconnect(Principal p, JsonElement args) {
        var id = Str(args, "id");
        if (bridges.TryGetValue(id, out var bridge) && bridge.Project == p.Project && bridge.Agent == p.Agent)
            bridges.Remove(id);
        return new { removed = true };
    }
    private object ActiveBridges(Principal p) {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        PruneOwners(now);
        var cutoff = now - 15000;
        foreach (var id in bridges.Where(pair => pair.Value.Seen < cutoff).Select(pair => pair.Key).ToArray()) bridges.Remove(id);
        return bridges.Values.Where(value => value.Project == p.Project).Select(value => {
            var identity = new Principal(value.Project, value.Agent);
            var binding = mailbox.FindIdentity(identity);
            return new { agent = value.Agent, mode = value.Mode, restoration = value.Restoration, lastSeenUnixMs = value.Seen, binding, topics = binding is null ? Array.Empty<string>() : mailbox.Subscriptions(identity), wake = binding is null ? null : mailbox.GetWakeStatus(identity), notifier = Notifier(value.Project, value.Agent) };
        }).ToArray();
    }

    // Waiters need only matching live MCP identities. Avoid building the full status
    // report and sweeping every bridge's inbox once a second for each waiter.
    private string[] ClaudeSessionBridges(Principal p, string sessionId) {
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 15000;
        return bridges.Values.Where(b => b.Project == p.Project && b.Mode == "mcp" && b.Seen >= cutoff)
            .Select(b => b.Agent).Distinct(StringComparer.Ordinal)
            .Where(agent => {
                var identity = mailbox.FindIdentity(new Principal(p.Project, agent));
                return identity is not null && Mailbox.ToolName(identity.Runtime) == "claude-code" && identity.SessionId == sessionId;
            }).ToArray();
    }

    private object Notifier(string project, string agent) {
        var key = (project, agent);
        string? active = owners.TryGetValue(key, out var owner) ? owner.Mode : null;
        object? lastRelease = released.TryGetValue(key, out var last) ? new { mode = last.Mode, reason = last.Reason, atUnixMs = last.At } : null;
        return new { active, lastRelease };
    }

    private static bool IsClaudeNotifier(string mode) => mode is "claude-channel" or "claude-monitor" or "claude-hook";

    private void PruneOwners(long now) {
        foreach (var (key, owner) in owners.Where(pair => pair.Value.Seen < now - OwnerTimeoutMilliseconds).ToArray()) {
            _ = owners.Remove(key);
            released[key] = (owner.Mode, "lost", now);
        }
        // A replaced holder that still polls stays refused; one that stopped is forgotten.
        foreach (var holder in superseded.Where(pair => pair.Value < now - OwnerTimeoutMilliseconds).Select(pair => pair.Key).ToArray())
            _ = superseded.Remove(holder);
    }

    // A newer holder of the same mode replaces the older one, as each Claude turn starts a
    // fresh hook waiter. A live holder of another mode is never displaced.
    private object WakeOwner(Principal p, JsonElement args) {
        var holder = Str(args, "holderId");
        var mode = Str(args, "mode");
        if (!Guid.TryParseExact(holder, "N", out _) || !IsClaudeNotifier(mode)) throw new ArgumentException();
        if (mailbox.FindIdentity(p) is null) throw new InvalidOperationException();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        PruneOwners(now);
        var key = (p.Project, p.Agent);
        if (owners.TryGetValue(key, out var current) && current.Holder != holder) {
            if (superseded.ContainsKey(holder)) superseded[holder] = now;
            if (current.Mode != mode || superseded.ContainsKey(holder))
                return new { owner = false, active = current.Mode };
            if (superseded.Count >= 1024) throw new InvalidOperationException();
            superseded[current.Holder] = now;
            released[key] = (current.Mode, "superseded", now);
        }
        else if (!owners.ContainsKey(key) && owners.Count >= 1024) throw new InvalidOperationException();
        _ = superseded.Remove(holder);
        owners[key] = (holder, mode, now);
        return new { owner = true, active = mode };
    }

    private object WakeOwnerCheck(Principal p, JsonElement args) {
        var holder = Str(args, "holderId");
        PruneOwners(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var owns = owners.TryGetValue((p.Project, p.Agent), out var owner) && owner.Holder == holder && owner.Mode == "claude-hook";
        if (!owns) return new { owner = false };
        var inbox = mailbox.Inbox(p);
        // An unsubscribed session with no outstanding deliveries has nothing to watch.
        return new { owner = mailbox.Subscriptions(p).Length > 0 || inbox.Queued > 0 || inbox.Leased > 0 };
    }

    private object WakeOwnerRelease(Principal p, JsonElement args) {
        var holder = Str(args, "holderId");
        var reason = Str(args, "reason");
        if (reason is not ("woke" or "renewed" or "expired" or "orphaned" or "stopped")) throw new ArgumentException();
        var key = (p.Project, p.Agent);
        if (!owners.TryGetValue(key, out var owner) || owner.Holder != holder) return new { released = false };
        _ = owners.Remove(key);
        released[key] = (owner.Mode, reason, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return new { released = true };
    }

    private WakeClaim? ClaimWake(Principal p, JsonElement args) {
        var mode = Str(args, "mode");
        if (IsClaudeNotifier(mode)) {
            PruneOwners(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var holder = Optional(args, "holderId");
            if (holder is null || !owners.TryGetValue((p.Project, p.Agent), out var owner) || owner.Holder != holder || owner.Mode != mode)
                return null;
        }
        return mailbox.ClaimWake(p, mode, Optional(args, "sessionId"));
    }

    private object Register(Principal p, JsonElement args) {
        mailbox.Register(p.Project, p.Agent, Str(args, "runtime"), Str(args, "sessionId"), Optional(args, "origin") ?? "unknown");
        return new { registered = true, project = p.Project, agent = p.Agent, runtimeVerified = false };
    }
    private (string Correlation, string Key, long Expires) MessageOptions(JsonElement args) {
        var correlation = Optional(args, "correlationId");
        var key = Optional(args, "idempotencyKey");
        var expiry = OptionalNumber(args, "expiresAtUnixMs");
        if (key is not null && (correlation is null || expiry is null))
            throw new ArgumentException("A retry key requires a fixed correlation ID and expiry");
        if (Optional(args, "replyTo") is not null && correlation is null)
            throw new ArgumentException("A reply requires the original correlation ID");
        return (correlation ?? Guid.NewGuid().ToString("N"), key ?? Guid.NewGuid().ToString("N"), expiry ?? checked(clock() + 86400000));
    }
    private object Send(Principal p, JsonElement args) {
        var options = MessageOptions(args);
        return mailbox.Send(p, Str(args, "recipient"), Str(args, "body"), options.Correlation, Optional(args, "replyTo"), options.Key, options.Expires, true, Optional(args, "topic"));
    }
    private object SessionSend(Principal p, JsonElement args) {
        var target = mailbox.RestoreSession(p.Project, Str(args, "recipientTool"), Str(args, "recipientSessionId")) ?? throw new InvalidOperationException("Unknown recipient session");
        var options = MessageOptions(args);
        return mailbox.Send(p, target.Agent, Str(args, "body"), options.Correlation, Optional(args, "replyTo"), options.Key, options.Expires, true, Optional(args, "topic"));
    }
    private object SessionLeave(Principal p, JsonElement args) {
        var route = mailbox.RestoreSession(p.Project, Str(args, "tool"), Str(args, "sessionId")) ?? throw new InvalidOperationException("Unknown session");
        mailbox.Subscribe(new Principal(p.Project, route.Agent), Str(args, "topic"), false);
        return new { subscribed = false };
    }
    private object Publish(Principal p, JsonElement args) {
        var options = MessageOptions(args);
        return mailbox.Publish(p, Str(args, "topic"), Str(args, "body"), options.Correlation, options.Key, options.Expires, true);
    }
    private object Subscribe(Principal p, JsonElement args) {
        var subscribed = !args.TryGetProperty("subscribed", out var v) || Bool(v);
        mailbox.Subscribe(p, Str(args, "topic"), subscribed);
        return new { subscribed };
    }
    private object CreateTopic(Principal p, JsonElement args) {
        var created = mailbox.CreateTopicForProject(p.Project, Str(args, "topic"));
        return new { created, warning = created ? null : "Topic already exists" };
    }
    private object JoinTopic(Principal p, JsonElement args) {
        mailbox.RegisterAndSubscribe(p, Str(args, "topic"), Str(args, "runtime"), Str(args, "sessionId"));
        return new { subscribed = true, project = p.Project, agent = p.Agent, runtimeVerified = false };
    }
    private object Pause(Principal p, JsonElement args) {
        if (!args.TryGetProperty("paused", out var v))
            throw new ArgumentException("Missing paused");
        var paused = Bool(v);
        mailbox.Pause(p, paused);
        return new { paused };
    }
    private static string Str(JsonElement e, string key) => Optional(e, key) ?? throw new ArgumentException("Required argument missing");
    private static string? Optional(JsonElement e, string key) => e.ValueKind != JsonValueKind.Object
            ? throw new ArgumentException("Arguments must be an object")
            : !e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null
            ? null
            : v.ValueKind != JsonValueKind.String ? throw new ArgumentException("Invalid argument type") : v.GetString();
    private static long? OptionalNumber(JsonElement e, string key) {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var value) ? value : throw new ArgumentException("Invalid number");
    }
    private static bool Bool(JsonElement e) => e.ValueKind switch {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ArgumentException("Invalid boolean")
    };
    public void Dispose() {
        lock (gate)
            mailbox.Dispose();
    }
}
