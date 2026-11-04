using System.Text.Json;

using Pigeon.Core;
using Pigeon.Service.Peers;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Pigeon.UnitTests")]

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
    // Antigravity has no busy state of its own: a message sent during a turn joins it and
    // derails the agent. Its hooks report turns here, and a wake is sent only to a session
    // known to be idle. The OpenCode plugin and the Copilot extension report their sessions' turns the same way. Kept only in memory, so after a restart a session waits for its next turn.
    // Woken marks a wake sent since the last hook report: if no turn starts, the wake's own
    // retry time decides when to send again, so a lost wake is retried and eventually gives up.
    private readonly Dictionary<string, (bool Busy, long Since, bool Woken)> turns = new(StringComparer.Ordinal);
    private long lastMaintenance, lastRequest;
    private const long BridgeTimeoutMilliseconds = 15000, OwnerTimeoutMilliseconds = 15000;
    // A Stop hook runs before the host finishes the turn, so a wake waits for the turn to settle.
    public const long IdleSettleMilliseconds = 2000;
    // Clients treat this error as a temporary service outage, not a rejected request.
    public const string StorageUnavailable = "storage unavailable";

    public Broker(string databasePath, Func<long>? clock = null, long? bodyRetentionMilliseconds = null, int maxAttempts = 3, int maxMessagesPerHour = 60, int maxWakeAttempts = 3, long wakeRetryMilliseconds = 120000, long abandonedTopicMilliseconds = 3600000, long leaseMilliseconds = 600000) {
        if (bodyRetentionMilliseconds is <= 0 || abandonedTopicMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(bodyRetentionMilliseconds));
        this.bodyRetentionMilliseconds = bodyRetentionMilliseconds;
        this.abandonedTopicMilliseconds = abandonedTopicMilliseconds;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        mailbox = new Mailbox(databasePath, clock, maxAttempts, maxMessagesPerHour, maxWakeAttempts, wakeRetryMilliseconds, leaseMilliseconds);
        try {
            mailbox.TouchAll();
        }
        catch {
            mailbox.Dispose();
            throw;
        }

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
                    "turn_state" => TurnState(args),
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
        if (now - lastRequest > 300000)
            mailbox.TouchAll();
        lastRequest = now;
        if (now - lastMaintenance < 60000)
            return;
        lastMaintenance = now;
        _ = mailbox.DeleteAbandoned(abandonedTopicMilliseconds);
        if (bodyRetentionMilliseconds is long retention)
            _ = mailbox.RedactTerminalBodies(retention);
    }

    private bool Connected(string session) {
        var cutoff = clock() - BridgeTimeoutMilliseconds;
        return bridges.Values.Any(b => b.Session == session && b.Seen >= cutoff);
    }

    private object Send(JsonElement args) {
        var topic = Str(args, "topic");
        var outcome = mailbox.Send(Str(args, "sessionId"), topic, Str(args, "message"), Optional(args, "retryKey") ?? Guid.NewGuid().ToString("N"), checked(clock() + 86400000));
        // A remote member ("@<peer>") is reached through the outbox; delivery to the peer happens later.
        var delivery = outcome.Recipient is null ? "no-recipient" : outcome.Recipient.SessionId.StartsWith('@') ? "queued-for-peer" : Connected(outcome.Recipient.SessionId) ? "sent" : "recipient-offline";
        return new { messageId = outcome.MessageId, delivery, text = Phrases.SendResult(delivery, topic) };
    }

    private object BridgeHeartbeat(JsonElement args) {
        var id = Str(args, "id");
        var mode = Str(args, "mode");
        var session = Optional(args, "sessionId");
        var restoration = Optional(args, "restoration");
        if (!Guid.TryParseExact(id, "N", out _) || mode is not ("mcp" or "codex-queue" or "claude-hook" or "antigravity-agentapi" or "opencode-prompt" or "copilot-extension"))
            throw new ArgumentException();
        if (restoration is not (null or "pending" or "bound" or "missing-session" or "not-configured" or "conflict"))
            throw new ArgumentException();
        var now = clock();
        foreach (var stale in bridges.Where(pair => pair.Value.Seen < now - BridgeTimeoutMilliseconds).Select(pair => pair.Key).ToArray())
            bridges.Remove(stale);
        if (!bridges.ContainsKey(id) && bridges.Count >= 1024)
            throw new InvalidOperationException("Too many connected bridges.");
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
        foreach (var id in bridges.Where(pair => pair.Value.Seen < now - BridgeTimeoutMilliseconds).Select(pair => pair.Key).ToArray())
            bridges.Remove(id);
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
        if (!Guid.TryParseExact(holder, "N", out _) || mode != "claude-hook")
            throw new ArgumentException();
        if (mailbox.Session(session) is null)
            throw new InvalidOperationException($"Session {session} is not known.");
        var now = clock();
        PruneOwners(now);
        if (owners.TryGetValue(session, out var current) && current.Holder != holder) {
            if (superseded.ContainsKey(holder)) {
                superseded[holder] = now;
                return new { owner = false, active = current.Mode };
            }

            if (superseded.Count >= 1024)
                throw new InvalidOperationException("Too many notifiers.");
            superseded[current.Holder] = now;
            released[session] = (current.Mode, "superseded", now);
        }
        else if (!owners.ContainsKey(session) && owners.Count >= 1024)
            throw new InvalidOperationException("Too many notifiers.");
        _ = superseded.Remove(holder);
        owners[session] = (holder, mode, now);
        return new { owner = true, active = mode };
    }

    private object WakeOwnerCheck(JsonElement args) {
        var session = Str(args, "sessionId");
        var holder = Str(args, "holderId");
        PruneOwners(clock());
        if (!owners.TryGetValue(session, out var owner) || owner.Holder != holder)
            return new { owner = false };
        // A session in no topic and with nothing waiting has nothing to watch.
        var status = mailbox.GetWakeStatus(session);
        return new { owner = mailbox.Session(session)?.Topics.Length > 0 || status.Queued + status.Leased > 0 };
    }

    private object WakeOwnerRelease(JsonElement args) {
        var session = Str(args, "sessionId");
        var holder = Str(args, "holderId");
        var reason = Str(args, "reason");
        if (reason is not ("woke" or "renewed" or "expired" or "orphaned" or "stopped"))
            throw new ArgumentException();
        if (!owners.TryGetValue(session, out var owner) || owner.Holder != holder)
            return new { released = false };
        _ = owners.Remove(session);
        released[session] = (owner.Mode, reason, clock());
        return new { released = true };
    }

    // The first report of a new turn also restores the session, since Antigravity has no
    // prompt hook that could do it and may not run SessionStart when a conversation is reopened.
    private object TurnState(JsonElement args) {
        var tool = Mailbox.ToolName(Str(args, "tool"));
        var session = Str(args, "sessionId");
        var state = Str(args, "state");
        if (tool is not ("antigravity" or "opencode" or "copilot") || state is not ("busy" or "idle") || session.Length > 256 || session.Any(char.IsControl))
            throw new ArgumentException();
        var busy = state == "busy";
        var started = busy && !(turns.TryGetValue(session, out var previous) && previous.Busy);
        if (!turns.ContainsKey(session) && turns.Count >= 1024) {
            // Forget the oldest idle sessions rather than refuse a live one.
            foreach (var key in turns.OrderBy(pair => pair.Value.Since).Take(256).Select(pair => pair.Key).ToArray())
                _ = turns.Remove(key);
        }

        // A repeated report keeps the time the state began, so a reporter may repeat its state
        // (the OpenCode plugin does, to restore it after a service restart) without delaying a wake.
        if (!(turns.TryGetValue(session, out var same) && same.Busy == busy && !same.Woken))
            turns[session] = (busy, clock(), false);
        var restored = started && mailbox.Restore(tool, session) is not null;
        return new { recorded = true, restored };
    }

    private WakeClaim? ClaimWake(JsonElement args) {
        var session = Str(args, "sessionId");
        var mode = Str(args, "mode");
        if (mode == "claude-hook") {
            PruneOwners(clock());
            var holder = Optional(args, "holderId");
            if (holder is null || !owners.TryGetValue(session, out var owner) || owner.Holder != holder)
                return null;
        }

        if (mode is "antigravity-agentapi" or "opencode-prompt" or "copilot-extension") {
            var now = clock();
            if (!turns.TryGetValue(session, out var turn) || (!turn.Woken && (turn.Busy || now - turn.Since < IdleSettleMilliseconds)))
                return null;
            var claim = mailbox.ClaimWake(session, mode);
            if (claim is not null)
                turns[session] = (true, now, true);
            return claim;
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

    // The peer link (see docs/remote-design.md). These are called by the peer listener and the
    // delivery loop under the same lock as every other operation, never through the local API.
    internal bool IsPeer(string fingerprint) {
        lock (gate)
            return mailbox.PeerByFingerprint(fingerprint) is not null;
    }

    internal bool HasPeers() {
        lock (gate)
            return mailbox.Peers().Length != 0;
    }

    internal bool RemovePeer(string name) {
        lock (gate)
            return mailbox.RemovePeer(name);
    }

    internal void SetPeerAddress(string name, string address) {
        lock (gate)
            mailbox.SetPeerAddress(name, address);
    }

    internal bool Unshare(string topic) {
        lock (gate)
            return mailbox.Unshare(topic);
    }

    // What `pigeon peer list` and `pigeon status` show; check codes of pending pairings are not
    // included, since the inviter must type the code the other person reads out.
    internal object PeerOverview(string? fingerprint) {
        lock (gate) {
            return new {
                name = mailbox.Setting("name"),
                listen = mailbox.Setting("listen"),
                fingerprint,
                peers = mailbox.Peers().Select(p => new { p.Name, p.Address, p.Fingerprint, p.LastContactUnixMs, outbox = mailbox.OutboxCount(p.Fingerprint) }).ToArray(),
                pending = mailbox.PendingPeers().Select(p => new { p.Name, p.ExpiresUnixMs }).ToArray(),
                shares = mailbox.Shares()
            };
        }
    }

    internal PeerInfo AddPeer(string fingerprint, string name, string? address) {
        lock (gate)
            return mailbox.AddPeer(fingerprint, name, address);
    }

    internal ShareInfo Share(string topic, string peerName, bool shareTool) {
        lock (gate)
            return mailbox.Share(topic, peerName, shareTool);
    }

    // Items delivered by a paired peer. Each result is "ok", "unknown" (no such share; the peer
    // drops the item) or "limited" (rate or size limit; the peer retries later).
    internal string[] FromPeer(string fingerprint, PeerItem[] items) {
        lock (gate) {
            Maintain();
            if (mailbox.PeerByFingerprint(fingerprint) is null)
                return [.. items.Select(_ => "unknown")];
            mailbox.TouchPeer(fingerprint);
            return [.. items.Select(item => {
                try {
                    var known = item.Kind switch {
                        "offer" => mailbox.AcceptOffer(fingerprint, item.Offered, item.SenderTool) is not null,
                        "unshare" => mailbox.ReceiveUnshare(fingerprint, item.Offered) || true,
                        "message" when item.MessageId is not null && item.Body is not null => mailbox.ReceiveFromPeer(fingerprint, item.Offered, item.MessageId, item.SenderTool, item.Body, item.ExpiresUnixMs),
                        _ => false
                    };
                    return known ? "ok" : "unknown";
                }
                catch (InvalidOperationException) {
                    return "limited";
                }
                catch (ArgumentException) {
                    return "unknown";
                }
            })];
        }
    }

    // Peers with an address and items due for them.
    internal (PeerInfo Peer, OutboxItem[] Items)[] DueForPeers() {
        lock (gate) {
            return [.. mailbox.Peers().Where(peer => peer.Address is not null)
                .Select(peer => (peer, mailbox.DueOutbox(peer.Fingerprint)))
                .Where(due => due.Item2.Length != 0)];
        }
    }

    // Open invitations live only in memory: a restart cancels them, which is the safe direction.
    private readonly List<(byte[] Secret, long Expires)> invitations = [];

    internal bool HasOpenInvitation() {
        lock (gate) {
            _ = invitations.RemoveAll(i => i.Expires <= clock());
            return invitations.Count != 0;
        }
    }

    internal Invitation CreateInvitation(string fingerprint, string address, string name) {
        lock (gate) {
            var invitation = new Invitation(address, fingerprint, name, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            invitations.Add((invitation.Secret, checked(clock() + (long)Invitation.Lifetime.TotalMilliseconds)));
            return invitation;
        }
    }

    // An acceptor presented a proof for one of the open invitations. On success the invitation is
    // used up, the acceptor waits for the inviter to confirm the check code, and this returns it.
    internal string? Pair(string inviter, string acceptor, string proof, string name, string? address) {
        lock (gate) {
            _ = invitations.RemoveAll(i => i.Expires <= clock());
            if (mailbox.PeerByFingerprint(acceptor) is not null || acceptor == inviter)
                return null;
            var match = invitations.FindIndex(i => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(Invitation.Proof(i.Secret, inviter, acceptor)), System.Text.Encoding.ASCII.GetBytes(proof)));
            if (match < 0)
                return null;
            var secret = invitations[match].Secret;
            invitations.RemoveAt(match);
            var check = Invitation.CheckCode(secret, inviter, acceptor);
            // The pending pairing lasts an hour, so the inviter can confirm later.
            mailbox.AddPendingPeer(acceptor, name, address, check, checked(clock() + 3600000));
            return check;
        }
    }

    internal PeerInfo ConfirmPendingPeer(string check, string? name) {
        lock (gate)
            return mailbox.ConfirmPendingPeer(check, name);
    }

    internal PendingPeer[] PendingPeers() {
        lock (gate)
            return mailbox.PendingPeers();
    }

    internal void SetPeerSetting(string key, string? value) {
        lock (gate)
            mailbox.SetSetting(key, value);
    }

    internal string? PeerSetting(string key) {
        lock (gate)
            return mailbox.Setting(key);
    }

    // "ok" delivered, "unknown" rejected for good, anything else (including no answer) retried later.
    internal void PeerOutcome(string fingerprint, string itemId, string? outcome) {
        lock (gate) {
            if (outcome == "ok")
                mailbox.OutboxDelivered(itemId);
            else if (outcome == "unknown")
                mailbox.OutboxRejected(itemId);
            else
                mailbox.OutboxFailed(itemId);
            if (outcome is "ok" or "unknown" or "limited")
                mailbox.TouchPeer(fingerprint);
        }
    }
}

// One item on the peer link: an offer, an unshare, or a message of a shared topic.
public sealed record PeerItem(string Kind, string Offered, string? MessageId, string? SenderTool, string? Body, long ExpiresUnixMs);

// Wording shared by the CLI and the MCP tools.
public static class Phrases {
    public static string SendResult(string delivery, string topic) => delivery switch {
        "sent" => "Sent.",
        "recipient-offline" => "The recipient is unavailable right now; your message will wait until it returns (up to 24 hours).",
        "queued-for-peer" => "Queued for the other machine; it is delivered when that machine is reachable (up to 24 hours).",
        _ => $"The recipient is unavailable: nobody else is in {topic}. Your message is held until someone joins (up to 24 hours)."
    };
}
