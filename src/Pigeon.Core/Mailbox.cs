using System.Globalization;

namespace Pigeon.Core;

// A session ID is a routing claim supplied by the host or the caller; it is not authentication.
public sealed record Member(string Tool, string SessionId);
public sealed record TopicInfo(string Topic, Member[] Members, int Waiting);
public sealed record SessionInfo(string Tool, string SessionId, string[] Topics);
public sealed record Delivery(string Id, string Topic, string SenderTool, string SenderSessionId, string Body, long SentAtUnixMs);
public sealed record SendOutcome(string MessageId, Member? Recipient);

public sealed class TopicFullException(TopicInfo topic)
    : InvalidOperationException($"Topic \"{topic.Topic}\" is full: {string.Join(" and ", topic.Members.Select(m => m.Tool + " " + m.SessionId))} are in it. Use another topic name, or have one of them leave first (pigeon leave {topic.Topic} as <session-id>).") {
    public TopicInfo Topic { get; } = topic;
}

// A topic is a private conversation between at most two sessions. Messages queue in order for
// the other member, and wait for whoever joins next while the other seat is empty.
public sealed partial class Mailbox : IDisposable {
    private readonly Sqlite db;
    private readonly Func<long> now;
    private readonly int maxAttempts, maxMessagesPerHour, maxWakeAttempts;
    private readonly long wakeRetryMilliseconds, leaseMilliseconds;

    public Mailbox(string path, Func<long>? clock = null, int maxAttempts = 3, int maxMessagesPerHour = 60, int maxWakeAttempts = 3, long wakeRetryMilliseconds = 120000, long leaseMilliseconds = 600000) {
        if (maxAttempts < 1 || maxMessagesPerHour < 1 || maxWakeAttempts < 1 || wakeRetryMilliseconds < 1 || leaseMilliseconds < 1)
            throw new ArgumentOutOfRangeException();
        this.maxAttempts = maxAttempts;
        this.maxMessagesPerHour = maxMessagesPerHour;
        this.maxWakeAttempts = maxWakeAttempts;
        this.wakeRetryMilliseconds = wakeRetryMilliseconds;
        this.leaseMilliseconds = leaseMilliseconds;
        now = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        db = new Sqlite(path);
        try {
            foreach (var sql in new[] {
                "CREATE TABLE IF NOT EXISTS pigeon_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)",
                "CREATE TABLE IF NOT EXISTS p_sessions(session TEXT PRIMARY KEY, tool TEXT NOT NULL, last_seen INTEGER NOT NULL)",
                "CREATE TABLE IF NOT EXISTS p_topics(topic TEXT PRIMARY KEY, created INTEGER NOT NULL)",
                "CREATE TABLE IF NOT EXISTS p_members(topic TEXT NOT NULL REFERENCES p_topics(topic), session TEXT NOT NULL REFERENCES p_sessions(session), joined INTEGER NOT NULL, PRIMARY KEY(topic,session))",
                // recipient is NULL while the other seat is empty; the next member to join receives it.
                "CREATE TABLE IF NOT EXISTS p_messages(id TEXT PRIMARY KEY, topic TEXT NOT NULL, sender TEXT NOT NULL, recipient TEXT, body TEXT NOT NULL, idem TEXT NOT NULL, created INTEGER NOT NULL, expires INTEGER NOT NULL, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, lease_until INTEGER, lease_generation TEXT, terminal_at INTEGER, body_digest TEXT, UNIQUE(sender,idem))",
                "CREATE TABLE IF NOT EXISTS p_wake_state(session TEXT PRIMARY KEY, generation TEXT NOT NULL, state TEXT NOT NULL, attempts INTEGER NOT NULL, next_at INTEGER NOT NULL, last_outcome TEXT, confirmed_at INTEGER, updated INTEGER NOT NULL)",
                "CREATE TABLE IF NOT EXISTS p_wake_attempts(session TEXT NOT NULL, generation TEXT NOT NULL, number INTEGER NOT NULL, attempted_at INTEGER NOT NULL, mode TEXT NOT NULL, outcome TEXT NOT NULL, PRIMARY KEY(session,generation,number))",
                "CREATE INDEX IF NOT EXISTS p_messages_recipient ON p_messages(recipient,status)",
                "CREATE INDEX IF NOT EXISTS p_messages_topic ON p_messages(topic,created)"
            })
                _ = db.Exec(sql);
            _ = db.Transaction(() => {
                if (db.Query("SELECT 1 FROM pigeon_metadata WHERE key='schema'").Count == 0) {
                    MigrateEarlierSchema();
                    _ = db.Exec("INSERT INTO pigeon_metadata(key,value) VALUES('schema','3')");
                }
                return 0;
            });
        }
        catch { db.Dispose(); throw; }
    }

    // Earlier versions kept name-based agents and multi-member topics. Their tables are left
    // untouched; only session identities, up to two members per topic (the most recently
    // active), and their pending messages are carried over.
    private void MigrateEarlierSchema() {
        if (db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='session_routes'").Count == 0) return;
        var sessions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var route in db.Query("SELECT agent,tool,session FROM session_routes ORDER BY rowid")) {
            if (db.Query("SELECT 1 FROM p_sessions WHERE session=?", route[2]).Count != 0) continue;
            _ = db.Exec("INSERT INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", route[2], ToolName(route[1])!, Time());
            sessions[route[0]!] = route[2]!;
        }
        var dropped = new List<string>();
        foreach (var topic in db.Query("SELECT DISTINCT topic FROM subscriptions ORDER BY topic").Select(row => row[0]!)) {
            var members = db.Query("SELECT s.agent, COALESCE((SELECT MAX(created) FROM messages m WHERE m.sender=s.agent OR m.recipient=s.agent),0) AS active FROM subscriptions s WHERE s.topic=? ORDER BY active DESC, s.rowid DESC", topic)
                .Select(row => row[0]!).Where(sessions.ContainsKey).ToArray();
            if (members.Length == 0) continue;
            _ = db.Exec("INSERT INTO p_topics(topic,created) VALUES(?,?)", topic, Time());
            foreach (var agent in members.Take(2))
                _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", topic, sessions[agent], Time());
            dropped.AddRange(members.Skip(2).Select(agent => topic + ": " + sessions[agent]));
        }
        foreach (var m in db.Query("SELECT id,topic,sender,recipient,body,idem,created,expires FROM messages WHERE status IN ('queued','leased') AND topic IS NOT NULL ORDER BY created,rowid")) {
            if (!sessions.TryGetValue(m[2]!, out var sender) || !sessions.TryGetValue(m[3]!, out var recipient)) continue;
            if (db.Query("SELECT 1 FROM p_members WHERE topic=? AND session=?", m[1], recipient).Count == 0) continue;
            _ = db.Exec("INSERT OR IGNORE INTO p_messages(id,topic,sender,recipient,body,idem,created,expires,status) VALUES(?,?,?,?,?,?,?,?,'queued')", m[0], m[1], sender, recipient, m[4], "migrated:" + m[5], m[6], m[7]);
            EnsureWake(recipient);
        }
        if (dropped.Count != 0)
            _ = db.Exec("INSERT OR REPLACE INTO pigeon_metadata(key,value) VALUES('upgrade_removed_members',?)", string.Join("; ", dropped));
    }

    private string Time() => now().ToString(CultureInfo.InvariantCulture);
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static long Long(string? value) => long.Parse(value!, CultureInfo.InvariantCulture);
    private static string Digest(string body) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
    private static void Required(string? s, string name, int max = 256) {
        if (string.IsNullOrWhiteSpace(s) || s.Length > max || s.Contains('\0') || (name != "message" && s.Any(char.IsControl)))
            throw new ArgumentException($"Invalid {name}");
    }

    // The single tool-name normalization: `Claude`, `claude_code` and `Claude Code` are all `claude-code`.
    public static string? ToolName(string? tool) {
        var name = tool?.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        return name == "claude" ? "claude-code" : name;
    }
    private static string CanonicalTool(string tool) {
        Required(tool, "tool");
        return ToolName(tool)!;
    }
    private static string NotMember(string session, string topic) => $"Session {session} is not in topic \"{topic}\".";

    private Member[] MembersOf(string topic) =>
        [.. db.Query("SELECT s.tool,m.session FROM p_members m JOIN p_sessions s ON s.session=m.session WHERE m.topic=? ORDER BY m.joined,m.rowid", topic).Select(r => new Member(r[0]!, r[1]!))];
    private string[] TopicsOf(string session) =>
        [.. db.Query("SELECT topic FROM p_members WHERE session=? ORDER BY topic", session).Select(r => r[0]!)];
    private TopicInfo Info(string topic, string? forSession = null) {
        var waiting = forSession is null
            ? db.Query("SELECT COUNT(*) FROM p_messages WHERE topic=? AND status IN ('queued','leased')", topic)
            : db.Query("SELECT COUNT(*) FROM p_messages WHERE topic=? AND recipient=? AND status IN ('queued','leased')", topic, forSession);
        return new(topic, MembersOf(topic), int.Parse(waiting[0][0]!, CultureInfo.InvariantCulture));
    }
    private string? ToolOf(string session) => db.Query("SELECT tool FROM p_sessions WHERE session=?", session).FirstOrDefault()?[0];
    private Delivery ToDelivery(string?[] r) => new(r[0]!, r[1]!, ToolOf(r[2]!) ?? "unknown", r[2]!, r[3]!, Long(r[4]));

    // Expire old messages and return timed-out leases to the queue. A lease keeps its
    // generation so the wake that took it can still acknowledge it on completion.
    private void Sweep() {
        var current = Time();
        _ = db.Exec("UPDATE p_messages SET status='expired',terminal_at=?,lease_until=NULL WHERE status IN ('queued','leased') AND expires<=?", current, current);
        _ = db.Exec("UPDATE p_messages SET terminal_at=CASE WHEN attempts>=? THEN ? ELSE NULL END,status=CASE WHEN attempts>=? THEN 'failed' ELSE 'queued' END,lease_until=NULL WHERE status='leased' AND lease_until<=?", Number(maxAttempts), current, Number(maxAttempts), current);
    }

    public TopicInfo Join(string toolName, string session, string topic) {
        Required(session, "session ID");
        Required(topic, "topic");
        var tool = CanonicalTool(toolName);
        return db.Transaction(() => {
            Sweep();
            var known = ToolOf(session);
            if (known is not null && known != tool)
                throw new InvalidOperationException($"Session {session} already joined using {known}.");
            if (known is null) _ = db.Exec("INSERT INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", session, tool, Time());
            else _ = db.Exec("UPDATE p_sessions SET last_seen=? WHERE session=?", Time(), session);
            var members = MembersOf(topic);
            if (members.Any(m => m.SessionId == session)) return Info(topic);
            if (members.Length >= 2) throw new TopicFullException(Info(topic));
            _ = db.Exec("INSERT OR IGNORE INTO p_topics(topic,created) VALUES(?,?)", topic, Time());
            _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", topic, session, Time());
            if (db.Exec("UPDATE p_messages SET recipient=? WHERE topic=? AND recipient IS NULL AND sender<>? AND status='queued'", session, topic, session) != 0)
                WakeForNewMessage(session);
            return Info(topic);
        });
    }

    // Returns true when the session was the last member and the topic was deleted.
    public bool Leave(string session, string topic) {
        Required(session, "session ID");
        Required(topic, "topic");
        return db.Transaction(() => {
            Sweep();
            if (db.Exec("DELETE FROM p_members WHERE topic=? AND session=?", topic, session) == 0)
                throw new InvalidOperationException(NotMember(session, topic));
            var deleted = db.Query("SELECT 1 FROM p_members WHERE topic=?", topic).Count == 0;
            if (deleted) DeleteTopicCore(topic);
            // Unread messages wait for whoever takes the leaver's place.
            else _ = db.Exec("UPDATE p_messages SET recipient=NULL,status='queued',lease_until=NULL WHERE topic=? AND recipient=? AND status IN ('queued','leased')", topic, session);
            SettleWake(session);
            return deleted;
        });
    }

    private void DeleteTopicCore(string topic) {
        var members = db.Query("SELECT session FROM p_members WHERE topic=?", topic).Select(r => r[0]!).ToArray();
        var recipients = db.Query("SELECT DISTINCT recipient FROM p_messages WHERE topic=? AND recipient IS NOT NULL", topic).Select(r => r[0]!).ToArray();
        _ = db.Exec("DELETE FROM p_messages WHERE topic=?", topic);
        _ = db.Exec("DELETE FROM p_members WHERE topic=?", topic);
        _ = db.Exec("DELETE FROM p_topics WHERE topic=?", topic);
        foreach (var session in members.Concat(recipients).Distinct(StringComparer.Ordinal)) SettleWake(session);
    }

    public TopicInfo[] Topics(string? session = null) => db.Transaction(() => {
        Sweep();
        var names = session is null
            ? db.Query("SELECT topic FROM p_topics ORDER BY topic").Select(r => r[0]!).ToArray()
            : TopicsOf(session);
        return names.Select(name => Info(name, session)).ToArray();
    });

    public SessionInfo? Session(string session) {
        Required(session, "session ID");
        var tool = ToolOf(session);
        return tool is null ? null : new SessionInfo(tool, session, TopicsOf(session));
    }

    // Rebinds a known session. A wake that gave up is re-armed, since the session is active again.
    public SessionInfo? Restore(string toolName, string session) {
        Required(session, "session ID");
        var tool = CanonicalTool(toolName);
        return db.Transaction(() => {
            if (ToolOf(session) != tool) return null;
            _ = db.Exec("UPDATE p_sessions SET last_seen=? WHERE session=?", Time(), session);
            Sweep();
            var (queued, _) = PendingCounts(session);
            if (queued > 0 && db.Exec("UPDATE p_wake_state SET generation=?,state='ready',attempts=0,next_at=?,last_outcome=NULL,confirmed_at=NULL,updated=? WHERE session=? AND state='exhausted'", NewGeneration(), Time(), Time(), session) == 0)
                EnsureWake(session);
            return new SessionInfo(tool, session, TopicsOf(session));
        });
    }

    public SendOutcome Send(string session, string topic, string body, string idempotency, long expires) {
        Required(session, "session ID");
        Required(topic, "topic");
        Required(body, "message", 16384);
        Required(idempotency, "retry key");
        return db.Transaction(() => {
            Sweep();
            var members = MembersOf(topic);
            if (!members.Any(m => m.SessionId == session)) throw new InvalidOperationException(NotMember(session, topic));
            var partner = members.FirstOrDefault(m => m.SessionId != session);
            var existing = db.Query("SELECT id,topic,body,body_digest FROM p_messages WHERE sender=? AND idem=?", session, idempotency).FirstOrDefault();
            if (existing is not null)
                return existing[1] != topic || (existing[3] is null ? existing[2] != body : existing[3] != Digest(body))
                    ? throw new InvalidOperationException("This retry key was already used for a different message.")
                    : new SendOutcome(existing[0]!, partner);
            if (expires <= now()) throw new InvalidOperationException("The message expiry must be in the future.");
            var sent = int.Parse(db.Query("SELECT COUNT(*) FROM p_messages WHERE topic=? AND created>?", topic, Number(checked(now() - 3600000)))[0][0]!, CultureInfo.InvariantCulture);
            if (sent >= maxMessagesPerHour)
                throw new InvalidOperationException($"Topic \"{topic}\" reached its limit of {maxMessagesPerHour} messages per hour; try again later.");
            var id = Guid.NewGuid().ToString("N");
            _ = db.Exec("INSERT INTO p_messages(id,topic,sender,recipient,body,idem,created,expires,status) VALUES(?,?,?,?,?,?,?,?,'queued')", id, topic, session, partner?.SessionId, body, idempotency, Time(), Number(expires));
            _ = db.Exec("UPDATE p_sessions SET last_seen=? WHERE session=?", Time(), session);
            if (partner is not null) WakeForNewMessage(partner.SessionId);
            return new SendOutcome(id, partner);
        });
    }

    // Reads and acknowledges in one step; used outside a wake, where nothing needs to survive a crash.
    public Delivery[] Take(string session, string topic, int max = 100) {
        Required(session, "session ID");
        Required(topic, "topic");
        return db.Transaction(() => {
            Sweep();
            if (db.Query("SELECT 1 FROM p_members WHERE topic=? AND session=?", topic, session).Count == 0)
                throw new InvalidOperationException(NotMember(session, topic));
            var rows = db.Query("SELECT id,topic,sender,body,created FROM p_messages WHERE topic=? AND recipient=? AND status='queued' ORDER BY created,rowid LIMIT ?", topic, session, Number(max));
            foreach (var row in rows)
                _ = db.Exec("UPDATE p_messages SET status='acknowledged',attempts=attempts+1,terminal_at=?,lease_until=NULL WHERE id=?", Time(), row[0]);
            _ = db.Exec("UPDATE p_sessions SET last_seen=? WHERE session=?", Time(), session);
            if (rows.Count != 0) SettleWake(session, progress: true);
            return rows.Select(ToDelivery).ToArray();
        });
    }

    public void Touch(string session) => db.Exec("UPDATE p_sessions SET last_seen=? WHERE session=?", Time(), session);

    // After a service restart or a suspended computer, every session gets a fresh grace period.
    public void TouchAll() => db.Exec("UPDATE p_sessions SET last_seen=?", Time());

    // A topic is abandoned when neither member has been seen for the idle period.
    public string[] DeleteAbandoned(long idleMilliseconds) {
        if (idleMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(idleMilliseconds));
        return db.Transaction(() => {
            var cutoff = Number(checked(now() - idleMilliseconds));
            var topics = db.Query("SELECT t.topic FROM p_topics t WHERE t.created<=? AND NOT EXISTS (SELECT 1 FROM p_members m JOIN p_sessions s ON s.session=m.session WHERE m.topic=t.topic AND s.last_seen>?)", cutoff, cutoff)
                .Select(r => r[0]!).ToArray();
            foreach (var topic in topics) DeleteTopicCore(topic);
            return topics;
        });
    }

    // Keep identifiers and digests so retention cannot reopen a retry key.
    public int RedactTerminalBodies(long retentionMilliseconds) {
        if (retentionMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(retentionMilliseconds));
        var cutoff = Number(checked(now() - retentionMilliseconds));
        return db.Transaction(() => {
            Sweep();
            var rows = db.Query("SELECT id,body FROM p_messages WHERE status IN ('acknowledged','expired','failed') AND terminal_at<=? AND body_digest IS NULL", cutoff);
            foreach (var row in rows)
                _ = db.Exec("UPDATE p_messages SET body='',body_digest=? WHERE id=?", Digest(row[1]!), row[0]);
            return rows.Count;
        });
    }

    public void Dispose() => db.Dispose();
}
