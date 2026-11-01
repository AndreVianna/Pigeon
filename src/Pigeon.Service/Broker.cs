using System.Text.Json;
using Pigeon.Core;

namespace Pigeon.Service;

public sealed record BrokerResult(bool Ok, object? Value, string? Error);

public sealed class Broker : IDisposable {
    private readonly Mailbox mailbox;
    private readonly long? bodyRetentionMilliseconds;
    private readonly long abandonedTopicMilliseconds;
    private readonly Func<long> clock;
    private readonly object gate = new();
    private readonly Dictionary<string, (string? Session, string Mode, string? Restoration, long Seen)> bridges = new(StringComparer.Ordinal);
    // One Claude notifier owns a session's wakes at a time. Ownership is a routing safeguard
    // kept only in memory; holders renew it every tick and reacquire it after a restart.
    private readonly Dictionary<string, (string Holder, string Mode, long Seen)> owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> superseded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Mode, string Reason, long At)> released = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> lastTouch = new(StringComparer.Ordinal);
    private long lastMaintenance, lastRequest;
    private const long BridgeTimeoutMilliseconds = 15000, OwnerTimeoutMilliseconds = 15000;
    // Clients treat this error as a temporary service outage, not a rejected request.
    public const string StorageUnavailable = "storage unavailable";

    public Broker(string databasePath, Func<long>? clock = null, long? bodyRetentionMilliseconds = null, int maxAttempts = 3, int maxMessagesPerHour = 60, int maxWakeAttempts = 3, long wakeRetryMilliseconds = 120000, long abandonedTopicMilliseconds = 3600000, long leaseMilliseconds = 600000) {
        if (bodyRetentionMilliseconds is <= 0 || abandonedTopicMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(bodyRetentionMilliseconds));
        this.bodyRetentionMilliseconds = bodyRetentionMilliseconds;
        this.abandonedTopicMilliseconds = abandonedTopicMilliseconds;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        mailbox = new Mailbox(databasePath, clock, maxAttempts, maxMessagesPerHour, maxWakeAttempts, wakeRetryMilliseconds, leaseMilliseconds);
        try { mailbox.TouchAll(); }
        catch { mailbox.Dispose(); throw; }
        lastMaintenance = lastRequest = this.clock();
    }

    internal BrokerResult Execute(string operation, JsonElement args) {
        lock (gate) {
            try {
                if (args.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("Arguments must be an object");
                Maintain();
                var result = operation switch {
                    "topics" => mailbox.Topics(Optional(args, "sessionId")),
                    "join" => mailbox.Join(Str(args, "tool"), Str(args, "sessionId"), Str(args, "topic")),
                    "leave" => new { deleted = mailbox.Leave(Str(args, "sessionId"), Str(args, "topic")) },
                    "send" => Send(args),
                    "take" => mailbox.Take(Str(args, "sessionId"), Str(args, "topic")),
                    "session" => mailbox.Session(Str(args, "sessionId")),
                    "session_restore" => mailbox.Restore(Str(args, "tool"), Str(args, "sessionId")),
                    "bridge_heartbeat" => BridgeHeartbeat(args),
                    "bridge_disconnect" => new { removed = bridges.Remove(Str(args, "id")) },
                    "bridges" => ActiveBridges(),
                    "claude_session_bridges" => ClaudeSessionBridges(Str(args, "sessionId")),
                    "wake_owner" => WakeOwner(args),
                    "wake_owner_check" => WakeOwnerCheck(args),
                    "wake_owner_release" => WakeOwnerRelease(args),
                    "wake_claim" => ClaimWake(args),
                    "wake_result" => mailbox.RecordWakeResult(Str(args, "sessionId"), Str(args, "generation"), checked((int)(OptionalNumber(args, "attempt") ?? throw new ArgumentException())), Str(args, "outcome")),
                    "wake_receive" => mailbox.ReceiveForWake(Str(args, "sessionId"), Str(args, "generation")),
                    "wake_complete" => mailbox.FinishWake(Str(args, "sessionId"), Str(args, "generation")),
                    "wake_status" => mailbox.GetWakeStatus(Str(args, "sessionId")),
                    "wake_states" => mailbox.PendingWakeStatuses(),
                    _ => throw new ArgumentException("Unknown operation")
                };
                return new(true, result, null);
            }
            catch (ArgumentException e) {
                return new(false, null, e.Message.StartsWith("Invalid ", StringComparison.Ordinal) ? e.Message + "." : "invalid request");
            }
            catch (OverflowException) {
                return new(false, null, "invalid request");
            }
            catch (InvalidOperationException e) {
                return new(false, null, e.Message);
            }
            catch (StorageUnavailableException) {
                return new(false, null, StorageUnavailable);
            }
        }
    }

    // Housekeeping runs before the requested operation, at most once a minute. A long gap since
    // the previous request means the service or computer was suspended: every session gets a
    // fresh grace period before any topic counts as abandoned.
    private void Maintain() {
        var now = clock();
        if (now - lastRequest > 300000) mailbox.TouchAll();
        lastRequest = now;
        if (now - lastMaintenance < 60000) return;
        lastMaintenance = now;
        _ = mailbox.DeleteAbandoned(abandonedTopicMilliseconds);
        if (bodyRetentionMilliseconds is long retention) _ = mailbox.RedactTerminalBodies(retention);
    }

    private bool Connected(string session) {
        var cutoff = clock() - BridgeTimeoutMilliseconds;
        return bridges.Values.Any(b => b.Session == session && b.Seen >= cutoff);
    }

    private object Send(JsonElement args) {
        var topic = Str(args, "topic");
        var outcome = mailbox.Send(Str(args, "sessionId"), topic, Str(args, "message"), Optional(args, "retryKey") ?? Guid.NewGuid().ToString("N"), checked(clock() + 86400000));
        var delivery = outcome.Recipient is null ? "no-recipient" : Connected(outcome.Recipient.SessionId) ? "sent" : "recipient-offline";
        return new { messageId = outcome.MessageId, delivery, text = Phrases.SendResult(delivery, topic) };
    }

    private object BridgeHeartbeat(JsonElement args) {
        var id = Str(args, "id");
        var mode = Str(args, "mode");
        var session = Optional(args, "sessionId");
        var restoration = Optional(args, "restoration");
        if (!Guid.TryParseExact(id, "N", out _) || mode is not ("mcp" or "codex-queue" or "claude-hook"))
            throw new ArgumentException();
        if (restoration is not (null or "pending" or "bound" or "missing-session" or "not-configured" or "conflict")) throw new ArgumentException();
        var now = clock();
        foreach (var stale in bridges.Where(pair => pair.Value.Seen < now - BridgeTimeoutMilliseconds).Select(pair => pair.Key).ToArray()) bridges.Remove(stale);
        if (!bridges.ContainsKey(id) && bridges.Count >= 1024) throw new InvalidOperationException("Too many connected bridges.");
        bridges[id] = (session, mode, restoration, now);
        // Record that the session is alive, at most once a minute.
        if (session is not null && now - lastTouch.GetValueOrDefault(session, long.MinValue / 2) >= 60000) {
            mailbox.Touch(session);
            lastTouch[session] = now;
        }
        return new { recorded = true };
    }

    private object ActiveBridges() {
        var now = clock();
        PruneOwners(now);
        foreach (var id in bridges.Where(pair => pair.Value.Seen < now - BridgeTimeoutMilliseconds).Select(pair => pair.Key).ToArray()) bridges.Remove(id);
        return bridges.Values.Select(value => {
            var session = value.Session is null ? null : mailbox.Session(value.Session);
            return new { sessionId = value.Session, tool = session?.Tool, mode = value.Mode, restoration = value.Restoration, lastSeenUnixMs = value.Seen, topics = session?.Topics ?? [], wake = session is null ? null : mailbox.GetWakeStatus(session.SessionId), notifier = session is null ? null : Notifier(session.SessionId) };
        }).ToArray();
    }

    // Hook waiters need to know only whether a live MCP bridge is bound to their Claude session.
    private bool ClaudeSessionBridges(string session) {
        var cutoff = clock() - BridgeTimeoutMilliseconds;
        return bridges.Values.Any(b => b.Mode == "mcp" && b.Session == session && b.Seen >= cutoff) && mailbox.Session(session)?.Tool == "claude-code";
    }

    private object Notifier(string session) {
        string? active = owners.TryGetValue(session, out var owner) ? owner.Mode : null;
        object? lastRelease = released.TryGetValue(session, out var last) ? new { mode = last.Mode, reason = last.Reason, atUnixMs = last.At } : null;
        return new { active, lastRelease };
    }

    private void PruneOwners(long now) {
        foreach (var (key, owner) in owners.Where(pair => pair.Value.Seen < now - OwnerTimeoutMilliseconds).ToArray()) {
            _ = owners.Remove(key);
            released[key] = (owner.Mode, "lost", now);
        }
        // A replaced holder that still polls stays refused; one that stopped is forgotten.
        foreach (var holder in superseded.Where(pair => pair.Value < now - OwnerTimeoutMilliseconds).Select(pair => pair.Key).ToArray())
            _ = superseded.Remove(holder);
    }

    // A newer holder replaces the older one, as each Claude turn starts a fresh hook waiter.
    private object WakeOwner(JsonElement args) {
        var session = Str(args, "sessionId");
        var holder = Str(args, "holderId");
        var mode = Str(args, "mode");
        if (!Guid.TryParseExact(holder, "N", out _) || mode != "claude-hook") throw new ArgumentException();
        if (mailbox.Session(session) is null) throw new InvalidOperationException($"Session {session} is not known.");
        var now = clock();
        PruneOwners(now);
        if (owners.TryGetValue(session, out var current) && current.Holder != holder) {
            if (superseded.ContainsKey(holder)) {
                superseded[holder] = now;
                return new { owner = false, active = current.Mode };
            }
            if (superseded.Count >= 1024) throw new InvalidOperationException("Too many notifiers.");
            superseded[current.Holder] = now;
            released[session] = (current.Mode, "superseded", now);
        }
        else if (!owners.ContainsKey(session) && owners.Count >= 1024) throw new InvalidOperationException("Too many notifiers.");
        _ = superseded.Remove(holder);
        owners[session] = (holder, mode, now);
        return new { owner = true, active = mode };
    }

    private object WakeOwnerCheck(JsonElement args) {
        var session = Str(args, "sessionId");
        var holder = Str(args, "holderId");
        PruneOwners(clock());
        if (!owners.TryGetValue(session, out var owner) || owner.Holder != holder) return new { owner = false };
        // A session in no topic and with nothing waiting has nothing to watch.
        var status = mailbox.GetWakeStatus(session);
        return new { owner = mailbox.Session(session)?.Topics.Length > 0 || status.Queued + status.Leased > 0 };
    }

    private object WakeOwnerRelease(JsonElement args) {
        var session = Str(args, "sessionId");
        var holder = Str(args, "holderId");
        var reason = Str(args, "reason");
        if (reason is not ("woke" or "renewed" or "expired" or "orphaned" or "stopped")) throw new ArgumentException();
        if (!owners.TryGetValue(session, out var owner) || owner.Holder != holder) return new { released = false };
        _ = owners.Remove(session);
        released[session] = (owner.Mode, reason, clock());
        return new { released = true };
    }

    private WakeClaim? ClaimWake(JsonElement args) {
        var session = Str(args, "sessionId");
        var mode = Str(args, "mode");
        if (mode == "claude-hook") {
            PruneOwners(clock());
            var holder = Optional(args, "holderId");
            if (holder is null || !owners.TryGetValue(session, out var owner) || owner.Holder != holder) return null;
        }
        return mailbox.ClaimWake(session, mode);
    }

    private static string Str(JsonElement e, string key) => Optional(e, key) ?? throw new ArgumentException("Required argument missing");
    private static string? Optional(JsonElement e, string key) => !e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null
        ? null
        : v.ValueKind != JsonValueKind.String ? throw new ArgumentException("Invalid argument type") : v.GetString();
    private static long? OptionalNumber(JsonElement e, string key) {
        if (!e.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var value) ? value : throw new ArgumentException("Invalid number");
    }
    public void Dispose() {
        lock (gate)
            mailbox.Dispose();
    }
}

// Wording shared by the CLI and the MCP tools.
public static class Phrases {
    public static string SendResult(string delivery, string topic) => delivery switch {
        "sent" => "Sent.",
        "recipient-offline" => "The recipient is unavailable right now; your message will wait until it returns (up to 24 hours).",
        _ => $"The recipient is unavailable: nobody else is in {topic}. Your message is held until someone joins (up to 24 hours)."
    };
}
