namespace Pigeon.Core;

// Principal contains caller-supplied routing labels; it is not an authenticated identity.
public sealed record Principal(string Project, string Agent);
public sealed record Envelope(string Id, string Sender, string Recipient, string Body, string CorrelationId, string? ReplyTo, string? Topic, int Attempt, string Receipt, string? SenderTool = null, string? SenderSessionId = null, string? RecipientTool = null, string? RecipientSessionId = null);
public sealed record DeliveryAttempt(int Number, long StartedAtUnixMs, string Outcome);
public sealed record DeliveryStatus(string Id, string State, string Sender, string Recipient, string CorrelationId, string? Topic, long CreatedAtUnixMs, long ExpiresAtUnixMs, DeliveryAttempt[] Attempts, string? SenderTool = null, string? SenderSessionId = null, string? RecipientTool = null, string? RecipientSessionId = null);
public sealed record IdentityBinding(string Project, string Agent, string Runtime, string SessionId, string Origin, bool RuntimeVerified);
public sealed record InboxStatus(bool ProjectPaused, int Queued, int Leased, int Acknowledged, int Expired, int Failed);
public sealed record TopicPost(long Sequence, string Id, string Sender, string Body, string CorrelationId, long ExpiresAtUnixMs, string? SenderTool = null, string? SenderSessionId = null);

public sealed partial class Mailbox : IDisposable {
    public const string TopicNamespace = "__pigeon_topics_v2__";
    private readonly Sqlite db;
    private readonly Func<long> now;
    private bool topicModeEnabled;
    private readonly int maxAttempts, maxAutomated, maxAutomatedPerMinute;
    private readonly int maxWakeAttempts;
    private readonly long wakeRetryMilliseconds;
    public Mailbox(string path, Func<long>? clock = null, int maxAttempts = 3, int maxAutomated = 4, int maxAutomatedPerMinute = 100, int maxWakeAttempts = 3, long wakeRetryMilliseconds = 120000) {
        if (maxAttempts < 1 || maxAutomated < 0 || maxAutomatedPerMinute < 0 || maxWakeAttempts < 1 || wakeRetryMilliseconds < 1)
            throw new ArgumentOutOfRangeException();
        this.maxAttempts = maxAttempts;
        this.maxAutomated = maxAutomated;
        this.maxAutomatedPerMinute = maxAutomatedPerMinute;
        this.maxWakeAttempts = maxWakeAttempts;
        this.wakeRetryMilliseconds = wakeRetryMilliseconds;
        now = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        db = new Sqlite(path);
        var needsTopicBackfill = db.Query("SELECT 1 FROM sqlite_master WHERE type='table' AND name='topics'").Count == 0;
        foreach (var sql in new[] {
            "CREATE TABLE IF NOT EXISTS agents(project TEXT NOT NULL, agent TEXT NOT NULL, runtime TEXT NOT NULL, session TEXT NOT NULL, PRIMARY KEY(project,agent))",
            "CREATE TABLE IF NOT EXISTS session_routes(project TEXT NOT NULL, tool TEXT NOT NULL, session TEXT NOT NULL, agent TEXT NOT NULL, PRIMARY KEY(project,tool,session), UNIQUE(project,agent), FOREIGN KEY(project,agent) REFERENCES agents(project,agent))",
            "CREATE TABLE IF NOT EXISTS projects(project TEXT PRIMARY KEY, paused INTEGER NOT NULL DEFAULT 0)",
            "CREATE TABLE IF NOT EXISTS topics(project TEXT NOT NULL, topic TEXT NOT NULL, PRIMARY KEY(project,topic), FOREIGN KEY(project) REFERENCES projects(project))",
            "CREATE TABLE IF NOT EXISTS messages(id TEXT PRIMARY KEY, project TEXT NOT NULL, sender TEXT NOT NULL, recipient TEXT NOT NULL, body TEXT NOT NULL, correlation TEXT NOT NULL, reply_to TEXT, topic TEXT, idem TEXT NOT NULL, automated INTEGER NOT NULL, created INTEGER NOT NULL, expires INTEGER NOT NULL, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, lease_until INTEGER, UNIQUE(project,sender,idem), FOREIGN KEY(project,sender) REFERENCES agents(project,agent), FOREIGN KEY(project,recipient) REFERENCES agents(project,agent))",
            "CREATE TABLE IF NOT EXISTS subscriptions(project TEXT NOT NULL, topic TEXT NOT NULL, agent TEXT NOT NULL, PRIMARY KEY(project,topic,agent), FOREIGN KEY(project,agent) REFERENCES agents(project,agent))",
            "CREATE TABLE IF NOT EXISTS publications(id TEXT PRIMARY KEY, project TEXT NOT NULL, sender TEXT NOT NULL, topic TEXT NOT NULL, idem TEXT NOT NULL, body TEXT NOT NULL, correlation TEXT NOT NULL, expires INTEGER NOT NULL, automated INTEGER NOT NULL, UNIQUE(project,sender,topic,idem), FOREIGN KEY(project,sender) REFERENCES agents(project,agent))",
            "CREATE TABLE IF NOT EXISTS publication_messages(publication TEXT NOT NULL REFERENCES publications(id), message TEXT NOT NULL REFERENCES messages(id), PRIMARY KEY(publication,message))",
            "CREATE TABLE IF NOT EXISTS attempts(receipt TEXT PRIMARY KEY, message TEXT NOT NULL REFERENCES messages(id), number INTEGER NOT NULL, started INTEGER NOT NULL, outcome TEXT NOT NULL)",
            "CREATE TABLE IF NOT EXISTS activity(seq INTEGER PRIMARY KEY AUTOINCREMENT, at INTEGER NOT NULL, project TEXT NOT NULL, event TEXT NOT NULL, message TEXT, detail TEXT)",
            "CREATE TABLE IF NOT EXISTS wake_state(project TEXT NOT NULL, agent TEXT NOT NULL, generation TEXT NOT NULL, state TEXT NOT NULL, attempts INTEGER NOT NULL, next_at INTEGER NOT NULL, last_outcome TEXT, confirmed_at INTEGER, updated INTEGER NOT NULL, PRIMARY KEY(project,agent))",
            "CREATE TABLE IF NOT EXISTS wake_attempts(project TEXT NOT NULL, agent TEXT NOT NULL, generation TEXT NOT NULL, number INTEGER NOT NULL, attempted_at INTEGER NOT NULL, mode TEXT NOT NULL, outcome TEXT NOT NULL, PRIMARY KEY(project,agent,generation,number))",
            "CREATE INDEX IF NOT EXISTS messages_automated_rate ON messages(project,automated,created)",
            "CREATE INDEX IF NOT EXISTS messages_automated_conversation ON messages(project,correlation,automated)",
            "CREATE INDEX IF NOT EXISTS activity_project_sequence ON activity(project,seq)"
        }) {
            _ = db.Exec(sql);
        }
        // Migrate prototype stores in place without discarding existing records.
        _ = db.Transaction(() => {
            AddColumn("messages", "terminal_at", "INTEGER");
            AddColumn("messages", "body_digest", "TEXT");
            AddColumn("publications", "body_digest", "TEXT");
            AddColumn("publications", "created", "INTEGER NOT NULL DEFAULT 0");
            AddColumn("agents", "origin", "TEXT NOT NULL DEFAULT 'unknown'");
            _ = db.Exec("INSERT OR IGNORE INTO projects(project) SELECT DISTINCT project FROM agents");
            if (needsTopicBackfill) {
                _ = db.Exec("INSERT OR IGNORE INTO topics(project,topic) SELECT project,topic FROM subscriptions");
                _ = db.Exec("INSERT OR IGNORE INTO topics(project,topic) SELECT project,topic FROM publications");
            }
            // Existing terminal records get a fresh retention window on upgrade.
            _ = db.Exec("UPDATE messages SET terminal_at=? WHERE terminal_at IS NULL AND status IN ('acknowledged','expired','failed')", Time());
            return 0;
        });
    }
    // The old schema had user-visible project namespaces. Consolidate only
    // when no names would be merged; otherwise leave all legacy rows intact.
    public void EnableTopicMode() {
        _ = db.Exec("CREATE TABLE IF NOT EXISTS pigeon_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
        if (db.Query("SELECT 1 FROM pigeon_metadata WHERE key='topic_mode' AND value='2'").Count != 0) { topicModeEnabled = true; return; }
        _ = db.Exec("PRAGMA foreign_keys=OFF");
        try {
            _ = db.Transaction(() => {
                if (db.Query("SELECT agent FROM agents GROUP BY agent HAVING COUNT(*)>1 LIMIT 1").Count != 0 ||
                    db.Query("SELECT topic FROM topics GROUP BY topic HAVING COUNT(*)>1 LIMIT 1").Count != 0)
                    throw new InvalidOperationException("Existing Pigeon namespaces contain duplicate agent or topic names; automatic migration cannot safely merge them. The database was not migrated.");
                var paused = db.Query("SELECT 1 FROM projects WHERE paused=1 LIMIT 1").Count != 0;
                _ = db.Exec("INSERT OR IGNORE INTO projects(project,paused) VALUES(?,?)", TopicNamespace, paused ? "1" : "0");
                if (paused) _ = db.Exec("UPDATE projects SET paused=1 WHERE project=?", TopicNamespace);
                foreach (var table in new[] { "agents", "session_routes", "topics", "subscriptions", "messages", "publications", "activity", "wake_state", "wake_attempts" })
                    _ = db.Exec("UPDATE " + table + " SET project=? WHERE project<>?", TopicNamespace, TopicNamespace);
                _ = db.Exec("DELETE FROM projects WHERE project<>?", TopicNamespace);
                if (db.Query("PRAGMA foreign_key_check").Count != 0) throw new InvalidOperationException("Pigeon migration failed integrity checks; the database was not migrated.");
                _ = db.Exec("INSERT INTO pigeon_metadata(key,value) VALUES('topic_mode','2')");
                return 0;
            });
        }
        finally { _ = db.Exec("PRAGMA foreign_keys=ON"); }
        topicModeEnabled = true;
    }
    private void CheckAutomatedRate(string project, int additional, string? topic = null) {
        var since = checked(now() - 60000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var filter = topicModeEnabled ? topic : null;
        var count = int.Parse(db.Query("SELECT COUNT(*) FROM messages WHERE project=? AND automated=1 AND created>? AND (? IS NULL OR topic=?)", project, since, filter, filter)[0][0]!);
        count += int.Parse(db.Query("SELECT COUNT(*) FROM publications p WHERE p.project=? AND p.automated=1 AND p.created>? AND (? IS NULL OR p.topic=?) AND NOT EXISTS (SELECT 1 FROM publication_messages pm WHERE pm.publication=p.id)", project, since, filter, filter)[0][0]!);
        if ((long)count + additional > maxAutomatedPerMinute)
            throw new InvalidOperationException("Automated project rate limit reached");
    }
    private void AddColumn(string table, string column, string definition) {
        if (!db.Query("PRAGMA table_info(" + table + ")").Any(row => row[1] == column))
            _ = db.Exec("ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition);
    }
    private static string Digest(string body) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
    private static bool SameBody(string body, string? stored, string? digest) => digest is null ? body == stored : Digest(body) == digest;
    private static void Required(string? s, string name, int max = 256) {
        if (string.IsNullOrWhiteSpace(s) || s.Length > max || s.Contains('\0') || (name != "body" && s.Any(char.IsControl)))
            throw new ArgumentException($"Invalid {name}");
    }
    private string Time() => now().ToString(System.Globalization.CultureInfo.InvariantCulture);
    private (string Display, string? Tool, string? Session) DisplayIdentity(string project, string agent) {
        var binding = db.Query("SELECT a.runtime,a.session,r.tool FROM agents a LEFT JOIN session_routes r ON r.project=a.project AND r.agent=a.agent WHERE a.project=? AND a.agent=?", project, agent).FirstOrDefault();
        if (binding is null) return (agent, null, null);
        var tool = binding[2] ?? binding[0];
        var generated = agent.Length == 72 && agent.StartsWith("session-", StringComparison.Ordinal) && agent.AsSpan(8).ToString().All(Uri.IsHexDigit);
        return (generated && binding[2] is not null ? tool! : agent, tool, binding[1]);
    }
    private void Log(string project, string evt, string? id = null, string? detail = null) => db.Exec("INSERT INTO activity(at,project,event,message,detail) VALUES(?,?,?,?,?)", Time(), project, evt, id, detail);
    private void Registered(Principal p) {
        Required(p.Project, "project");
        Required(p.Agent, "agent");
        if (db.Query("SELECT 1 FROM agents WHERE project=? AND agent=?", p.Project, p.Agent).Count == 0)
            throw new InvalidOperationException("Unknown project or agent");
    }
    public void Register(string project, string agent, string runtime, string session, string origin = "unknown") {
        Required(project, "project");
        Required(agent, "agent");
        Required(runtime, "runtime");
        Required(session, "session");
        if (origin is not ("unknown" or "existing" or "new"))
            throw new ArgumentException("Invalid session origin");
        _ = db.Transaction(() => {
            RegisterCore(project, agent, runtime, session, origin);
            return 0;
        });
    }
    private void RegisterCore(string project, string agent, string runtime, string session, string origin) {
        if (agent == "__pigeon__") throw new ArgumentException("Reserved agent name");
        _ = db.Exec("INSERT OR IGNORE INTO projects(project) VALUES(?)", project);
        var existing = db.Query("SELECT runtime,session,origin FROM agents WHERE project=? AND agent=?", project, agent);
        if (existing.Count != 0 && (existing[0][0] != runtime || existing[0][1] != session))
            throw new InvalidOperationException("Identity is already bound to a different session");
        if (existing.Count != 0) {
            if (origin != "unknown" && existing[0][2] != "unknown" && existing[0][2] != origin)
                throw new InvalidOperationException("Identity already has a different session-origin claim");
            if (origin != "unknown" && existing[0][2] == "unknown") {
                _ = db.Exec("UPDATE agents SET origin=? WHERE project=? AND agent=?", origin, project, agent);
                Log(project, "session-origin-claimed", null, agent);
            }
            return;
        }
        if (topicModeEnabled) {
            var tool = CanonicalTool(runtime);
            var equivalent = tool == "claude-code" ? "claude" : tool;
            if (db.Query("SELECT 1 FROM agents WHERE project=? AND session=? AND lower(replace(replace(runtime,'_','-'),' ','-')) IN (?,?) AND agent<>'__pigeon__'", project, session, tool, equivalent).Count != 0)
                throw new InvalidOperationException("Tool and session are already registered under another identity");
        }
        _ = db.Exec("INSERT INTO agents(project,agent,runtime,session,origin) VALUES(?,?,?,?,?)", project, agent, runtime, session, origin);
        Log(project, "registered", null, agent);
    }
    public void RegisterAndSubscribe(Principal p, string topic, string runtime, string session) {
        Required(p.Project, "project");
        Required(p.Agent, "agent");
        Required(topic, "topic");
        Required(runtime, "runtime");
        Required(session, "session");
        _ = db.Transaction(() => {
            if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", p.Project, topic).Count == 0)
                throw new InvalidOperationException("Unknown topic");
            RegisterCore(p.Project, p.Agent, runtime, session, "unknown");
            if (db.Exec("INSERT OR IGNORE INTO subscriptions(project,topic,agent) VALUES(?,?,?)", p.Project, topic, p.Agent) != 0)
                Log(p.Project, "subscribed", null, p.Agent + " @ " + topic);
            return 0;
        });
    }
    // Registration records a claim; it cannot prove ownership or liveness.
    public IdentityBinding Identity(Principal p) {
        Registered(p);
        var row = db.Query("SELECT runtime,session,origin FROM agents WHERE project=? AND agent=?", p.Project, p.Agent)[0];
        return new IdentityBinding(p.Project, p.Agent, row[0]!, row[1]!, row[2]!, false);
    }
    public IdentityBinding? FindIdentity(Principal p) {
        Required(p.Project, "project");
        Required(p.Agent, "agent");
        var row = db.Query("SELECT runtime,session,origin FROM agents WHERE project=? AND agent=?", p.Project, p.Agent).FirstOrDefault();
        return row is null ? null : new IdentityBinding(p.Project, p.Agent, row[0]!, row[1]!, row[2]!, false);
    }
    public void Pause(Principal p, bool paused) => db.Transaction(() => {
        Registered(p);
        _ = db.Exec("UPDATE projects SET paused=? WHERE project=?", paused ? "1" : "0", p.Project);
        Log(p.Project, paused ? "paused" : "resumed");
        return 0;
    });
    public string Send(Principal p, string recipient, string body, string correlation, string? replyTo, string idempotency, long expires, bool automated = false, string? topic = null) {
        Required(recipient, "recipient");
        Required(body, "body", 16384);
        Required(correlation, "correlation");
        Required(idempotency, "idempotency");
        if (replyTo != null)
            Required(replyTo, "reply-to");
        if (topic != null) Required(topic, "topic");
        return db.Transaction(() => {
            Registered(p);
            Registered(new Principal(p.Project, recipient));
            if (topicModeEnabled && recipient == p.Agent) throw new InvalidOperationException("Cannot send a message to the same session");
            var existing = db.Query("SELECT id,recipient,body,correlation,reply_to,expires,automated,body_digest,topic FROM messages WHERE project=? AND sender=? AND idem=?", p.Project, p.Agent, "D:" + idempotency);
            if (existing.Count != 0) {
                var e = existing[0];
                return e[1] != recipient || !SameBody(body, e[2], e[7]) || e[3] != correlation || e[4] != replyTo || e[5] != expires.ToString() || e[6] != (automated ? "1" : "0") || (topic != null && e[8] != topic)
                    ? throw new InvalidOperationException("Idempotency key reused with different content")
                    : e[0]!;
            }
            if (topicModeEnabled) {
                var shared = db.Query("SELECT a.topic FROM subscriptions a JOIN subscriptions b ON a.project=b.project AND a.topic=b.topic WHERE a.project=? AND a.agent=? AND b.agent=? AND (? IS NULL OR a.topic=?) ORDER BY a.topic", p.Project, p.Agent, recipient, topic, topic);
                if (shared.Count != 1) throw new InvalidOperationException("A direct message needs exactly one shared topic; specify the topic when necessary");
                topic = shared[0][0];
            }

            if (expires <= now())
                throw new InvalidOperationException("Expiry must be in the future");
            if (db.Query("SELECT 1 FROM projects WHERE project=? AND paused=1", p.Project).Count != 0)
                throw new InvalidOperationException("Project paused");
            if (replyTo != null && db.Query("SELECT 1 FROM messages WHERE id=? AND project=? AND correlation=? AND recipient=? AND (? IS NULL OR topic=?)", replyTo, p.Project, correlation, p.Agent, topicModeEnabled ? topic : null, topicModeEnabled ? topic : null).Count == 0)
                throw new InvalidOperationException("Reply target is not a message to this agent in this conversation");
            var filter = topicModeEnabled ? topic : null;
            var emptyPosts = int.Parse(db.Query("SELECT COUNT(*) FROM publications p WHERE p.project=? AND p.correlation=? AND p.automated=1 AND (? IS NULL OR p.topic=?) AND NOT EXISTS (SELECT 1 FROM publication_messages pm WHERE pm.publication=p.id)", p.Project, correlation, filter, filter)[0][0]!);
            if (automated && int.Parse(db.Query("SELECT COUNT(*) FROM messages WHERE project=? AND correlation=? AND automated=1 AND (? IS NULL OR topic=?)", p.Project, correlation, filter, filter)[0][0]!) + emptyPosts >= maxAutomated)
                throw new InvalidOperationException("Automated exchange limit reached");
            if (automated)
                CheckAutomatedRate(p.Project, 1, topic);
            var id = Guid.NewGuid().ToString("N");
            _ = db.Exec("INSERT INTO messages(id,project,sender,recipient,body,correlation,reply_to,topic,idem,automated,created,expires,status) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,'queued')", id, p.Project, p.Agent, recipient, body, correlation, replyTo, topic, "D:" + idempotency, automated ? "1" : "0", Time(), expires.ToString());
            WakeForNewMessage(p.Project, recipient);
            Log(p.Project, "stored", id, p.Agent + " -> " + recipient);
            return id;
        });
    }
    public void Subscribe(Principal p, string topic, bool subscribed = true) {
        Required(topic, "topic");
        _ = db.Transaction(() => {
            Registered(p);
            if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", p.Project, topic).Count == 0)
                throw new InvalidOperationException("Unknown topic");
            _ = subscribed
                ? db.Exec("INSERT OR IGNORE INTO subscriptions(project,topic,agent) VALUES(?,?,?)", p.Project, topic, p.Agent)
                : db.Exec("DELETE FROM subscriptions WHERE project=? AND topic=? AND agent=?", p.Project, topic, p.Agent);
            Log(p.Project, subscribed ? "subscribed" : "unsubscribed", null, p.Agent + " @ " + topic);
            return 0;
        });
    }
    public void CreateTopic(Principal p, string topic) {
        Required(topic, "topic");
        _ = db.Transaction(() => {
            Registered(p);
            CreateTopicCore(p.Project, topic);
            return 0;
        });
    }
    public bool CreateTopicForProject(string project, string topic) {
        Required(project, "project");
        Required(topic, "topic");
        return db.Transaction(() => {
            _ = db.Exec("INSERT OR IGNORE INTO projects(project) VALUES(?)", project);
            return CreateTopicCore(project, topic);
        });
    }
    private bool CreateTopicCore(string project, string topic) {
        var created = db.Exec("INSERT OR IGNORE INTO topics(project,topic) VALUES(?,?)", project, topic) != 0;
        if (created)
            Log(project, "topic-created", null, topic);
        return created;
    }
    public bool DeleteTopicForProject(string project, string topic) {
        Required(project, "project");
        Required(topic, "topic");
        return db.Transaction(() => DeleteTopicCore(project, topic));
    }
    private bool DeleteTopicCore(string project, string topic) {
        Sweep(project);
        if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", project, topic).Count == 0) return false;
        var subscribers = db.Query("SELECT agent FROM subscriptions WHERE project=? AND topic=? ORDER BY agent", project, topic);
        if (subscribers.Count != 0) {
            _ = db.Exec("INSERT OR IGNORE INTO agents(project,agent,runtime,session,origin) VALUES(?,'__pigeon__','pigeon','service','unknown')", project);
            var expires = checked(now() + 7L * 86400000).ToString();
            foreach (var subscriber in subscribers) {
                var id = Guid.NewGuid().ToString("N");
                _ = db.Exec("INSERT INTO messages(id,project,sender,recipient,body,correlation,reply_to,topic,idem,automated,created,expires,status) VALUES(?,?,'__pigeon__',?,?,?,NULL,?,?,0,?,?,'queued')", id, project, subscriber[0], "Topic closed: " + topic, "topic-closed-" + id, topic, "C:" + id, Time(), expires);
                WakeForNewMessage(project, subscriber[0]!);
                Log(project, "topic-closure-queued", id, subscriber[0]);
            }
        }
        _ = db.Exec("DELETE FROM subscriptions WHERE project=? AND topic=?", project, topic);
        var removed = db.Exec("DELETE FROM topics WHERE project=? AND topic=?", project, topic) != 0;
        if (removed)
            Log(project, "topic-deleted", null, topic);
        return removed;
    }
    public string[] TopicsForProject(string project) {
        Required(project, "project");
        return [.. db.Query("SELECT topic FROM topics WHERE project=? ORDER BY topic", project).Select(row => row[0]!)];
    }
    public string[] Subscriptions(Principal p) {
        Registered(p);
        return [.. db.Query("SELECT topic FROM subscriptions WHERE project=? AND agent=? ORDER BY topic", p.Project, p.Agent).Select(row => row[0]!)];
    }
    public InboxStatus Inbox(Principal p) => db.Transaction(() => {
        Registered(p);
        Sweep(p.Project);
        var paused = db.Query("SELECT paused FROM projects WHERE project=?", p.Project)[0][0] == "1";
        var counts = db.Query("SELECT status,COUNT(*) FROM messages WHERE project=? AND recipient=? GROUP BY status", p.Project, p.Agent)
            .ToDictionary(row => row[0]!, row => int.Parse(row[1]!));
        int Count(string status) => counts.GetValueOrDefault(status);
        return new InboxStatus(paused, Count("queued"), Count("leased"), Count("acknowledged"), Count("expired"), Count("failed"));
    });
    // Snapshot membership at publication time. Later subscribers do not receive past messages.
    public string[] Publish(Principal p, string topic, string body, string correlation, string idempotency, long expires, bool automated = false) {
        Required(topic, "topic");
        Required(body, "body", 16384);
        Required(correlation, "correlation");
        Required(idempotency, "idempotency");
        return db.Transaction(() => {
            Registered(p);
            if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", p.Project, topic).Count == 0)
                throw new InvalidOperationException("Unknown topic");
            var existing = db.Query("SELECT id,body,correlation,expires,automated,body_digest FROM publications WHERE project=? AND sender=? AND topic=? AND idem=?", p.Project, p.Agent, topic, idempotency);
            if (existing.Count != 0) {
                var e = existing[0];
                return !SameBody(body, e[1], e[5]) || e[2] != correlation || e[3] != expires.ToString() || e[4] != (automated ? "1" : "0")
                    ? throw new InvalidOperationException("Idempotency key reused with different publication")
                    : db.Query("SELECT message FROM publication_messages WHERE publication=? ORDER BY message", e[0]).Select(r => r[0]!).ToArray();
            }

            if (expires <= now())
                throw new InvalidOperationException("Expiry must be in the future");
            if (db.Query("SELECT 1 FROM projects WHERE project=? AND paused=1", p.Project).Count != 0)
                throw new InvalidOperationException("Project paused");
            var subscribers = db.Query("SELECT agent FROM subscriptions WHERE project=? AND topic=? AND agent<>? ORDER BY agent", p.Project, topic, p.Agent);
            var filter = topicModeEnabled ? topic : null;
            var emptyPosts = int.Parse(db.Query("SELECT COUNT(*) FROM publications p WHERE p.project=? AND p.correlation=? AND p.automated=1 AND (? IS NULL OR p.topic=?) AND NOT EXISTS (SELECT 1 FROM publication_messages pm WHERE pm.publication=p.id)", p.Project, correlation, filter, filter)[0][0]!);
            var cost = Math.Max(1, subscribers.Count);
            if (automated && int.Parse(db.Query("SELECT COUNT(*) FROM messages WHERE project=? AND correlation=? AND automated=1 AND (? IS NULL OR topic=?)", p.Project, correlation, filter, filter)[0][0]!) + emptyPosts + cost > maxAutomated)
                throw new InvalidOperationException("Automated exchange limit reached");
            if (automated)
                CheckAutomatedRate(p.Project, cost, topic);
            var publication = Guid.NewGuid().ToString("N");
            _ = db.Exec("INSERT INTO publications(id,project,sender,topic,idem,body,correlation,expires,automated,created) VALUES(?,?,?,?,?,?,?,?,?,?)", publication, p.Project, p.Agent, topic, idempotency, body, correlation, expires.ToString(), automated ? "1" : "0", Time());
            var ids = new List<string>();
            foreach (var subscriber in subscribers) {
                var id = Guid.NewGuid().ToString("N");
                ids.Add(id);
                _ = db.Exec("INSERT INTO messages(id,project,sender,recipient,body,correlation,reply_to,topic,idem,automated,created,expires,status) VALUES(?,?,?,?,?,?,NULL,?,?,?,?,?,'queued')", id, p.Project, p.Agent, subscriber[0], body, correlation, topic, "P:" + publication + ":" + subscriber[0], automated ? "1" : "0", Time(), expires.ToString());
                WakeForNewMessage(p.Project, subscriber[0]!);
                _ = db.Exec("INSERT INTO publication_messages(publication,message) VALUES(?,?)", publication, id);
                Log(p.Project, "stored", id, p.Agent + " -> " + subscriber[0] + " @ " + topic);
            }

            return [.. ids.OrderBy(x => x, StringComparer.Ordinal)];
        });
    }
    // Pull is a non-destructive view of publications, not a recipient delivery or acknowledgment.
    public TopicPost[] ReadTopic(Principal p, string topic, long? afterSequence, int limit = 3) {
        Registered(p);
        Required(topic, "topic");
        if (afterSequence is < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(limit));
        if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", p.Project, topic).Count == 0) throw new InvalidOperationException("Unknown topic");
        if (db.Query("SELECT 1 FROM projects WHERE project=? AND paused=1", p.Project).Count != 0) return [];
        return db.Query("SELECT rowid,id,sender,body,correlation,expires FROM publications WHERE project=? AND topic=? AND sender<>? AND expires>? AND body_digest IS NULL AND rowid>? ORDER BY rowid LIMIT ?", p.Project, topic, p.Agent, Time(), (afterSequence ?? 0).ToString(), limit.ToString()).Select(r => {
            var sender = DisplayIdentity(p.Project, r[2]!);
            return new TopicPost(long.Parse(r[0]!), r[1]!, sender.Display, r[3]!, r[4]!, long.Parse(r[5]!), sender.Tool, sender.Session);
        }).ToArray();
    }
    private void Sweep(string project) {
        // The outcome of each expired lease remains visible even when its message expires.
        var current = Time();
        var expired = db.Query("SELECT id FROM messages WHERE project=? AND status IN ('queued','leased') AND expires<=?", project, current);
        var failed = db.Query("SELECT id FROM messages WHERE project=? AND status='leased' AND lease_until<=? AND expires>? AND attempts>=?", project, current, current, maxAttempts.ToString());
        _ = db.Exec("UPDATE attempts SET outcome='expired' WHERE outcome='pending' AND message IN (SELECT id FROM messages WHERE project=? AND status='leased' AND expires<=? AND expires<=lease_until)", project, current);
        _ = db.Exec("UPDATE attempts SET outcome='timed_out' WHERE outcome='pending' AND message IN (SELECT id FROM messages WHERE project=? AND status='leased' AND lease_until<=? AND lease_until<expires)", project, current);
        _ = db.Exec("UPDATE messages SET status='expired',terminal_at=?,lease_until=NULL WHERE project=? AND status IN ('queued','leased') AND expires<=?", current, project, current);
        _ = db.Exec("UPDATE messages SET terminal_at=CASE WHEN attempts>=? THEN ? ELSE NULL END,status=CASE WHEN attempts>=? THEN 'failed' ELSE 'queued' END,lease_until=NULL WHERE project=? AND status='leased' AND lease_until<=?", maxAttempts.ToString(), current, maxAttempts.ToString(), project, current);
        foreach (var row in expired)
            Log(project, "expired", row[0]);
        foreach (var row in failed)
            Log(project, "failed", row[0], "delivery attempts exhausted");
    }
    public Envelope? Receive(Principal p, long leaseMilliseconds = 30000, string? topic = null) {
        if (leaseMilliseconds is < 1 or > 3600000)
            throw new ArgumentOutOfRangeException(nameof(leaseMilliseconds));
        if (topic != null)
            Required(topic, "topic");
        return db.Transaction(() => {
            Registered(p);
            Sweep(p.Project);
            if (db.Query("SELECT 1 FROM projects WHERE project=? AND paused=1", p.Project).Count != 0)
                return null;
            var rows = db.Query("SELECT id,sender,recipient,body,correlation,reply_to,attempts,topic FROM messages WHERE project=? AND recipient=? AND status='queued' AND (? IS NULL OR topic=?) ORDER BY created,id LIMIT 1", p.Project, p.Agent, topic, topic);
            if (rows.Count == 0)
                return null;
            var r = rows[0];
            var receipt = Guid.NewGuid().ToString("N");
            var attempt = int.Parse(r[6]!) + 1;
            _ = db.Exec("UPDATE messages SET status='leased',attempts=?,lease_until=? WHERE id=?", attempt.ToString(), checked(now() + leaseMilliseconds).ToString(), r[0]);
            _ = db.Exec("INSERT INTO attempts(receipt,message,number,started,outcome) VALUES(?,?,?,?,'pending')", receipt, r[0], attempt.ToString(), Time());
            Log(p.Project, "polled", r[0], "attempt " + attempt);
            var sender = DisplayIdentity(p.Project, r[1]!);
            var recipient = DisplayIdentity(p.Project, r[2]!);
            return new Envelope(r[0]!, sender.Display, recipient.Display, r[3]!, r[4]!, r[5], r[7], attempt, receipt, sender.Tool, sender.Session, recipient.Tool, recipient.Session);
        });
    }
    public bool Ack(Principal p, string receipt) {
        Required(receipt, "receipt");
        return db.Transaction(() => {
            Registered(p);
            Sweep(p.Project);
            var r = db.Query("SELECT m.id,m.status,a.outcome FROM attempts a JOIN messages m ON m.id=a.message WHERE a.receipt=? AND m.project=? AND m.recipient=?", receipt, p.Project, p.Agent);
            if (r.Count == 0)
                return false;
            if (r[0][1] == "acknowledged")
                return r[0][2] == "acknowledged";
            if (r[0][1] != "leased" || r[0][2] != "pending")
                return false;
            _ = db.Exec("UPDATE attempts SET outcome='acknowledged' WHERE receipt=?", receipt);
            _ = db.Exec("UPDATE messages SET status='acknowledged',terminal_at=?,lease_until=NULL WHERE id=?", Time(), r[0][0]);
            WakeDeliveryAcknowledged(p);
            Log(p.Project, "acknowledged", r[0][0], p.Agent);
            return true;
        });
    }
    public string? Status(Principal p, string id) => db.Transaction(() => {
        Registered(p);
        Sweep(p.Project);
        return db.Query("SELECT status FROM messages WHERE project=? AND id=? AND (sender=? OR recipient=?)", p.Project, id, p.Agent, p.Agent).FirstOrDefault()?[0];
    });
    public DeliveryStatus? Details(Principal p, string id) => db.Transaction(() => {
        Registered(p);
        Sweep(p.Project);
        var row = db.Query("SELECT id,status,sender,recipient,correlation,topic,created,expires FROM messages WHERE project=? AND id=? AND (sender=? OR recipient=?)", p.Project, id, p.Agent, p.Agent).FirstOrDefault();
        if (row is null)
            return null;
        var attempts = db.Query("SELECT number,started,outcome FROM attempts WHERE message=? ORDER BY number", id)
            .Select(a => new DeliveryAttempt(int.Parse(a[0]!), long.Parse(a[1]!), a[2]!)).ToArray();
        var sender = DisplayIdentity(p.Project, row[2]!);
        var recipient = DisplayIdentity(p.Project, row[3]!);
        return new DeliveryStatus(row[0]!, row[1]!, sender.Display, recipient.Display, row[4]!, row[5], long.Parse(row[6]!), long.Parse(row[7]!), attempts, sender.Tool, sender.Session, recipient.Tool, recipient.Session);
    });
    // Preserve identifiers, attempts and exchange counts so retention cannot
    // reopen an idempotency key or reset an automated-exchange limit.
    public int RedactTerminalBodies(Principal p, long retentionMilliseconds) {
        if (retentionMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(retentionMilliseconds));
        var cutoff = checked(now() - retentionMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return db.Transaction(() => {
            Registered(p);
            Sweep(p.Project);
            // Keep every copy of a publication until all subscriber deliveries
            // have reached a terminal state and their retention window elapsed.
            var messages = db.Query("SELECT m.id,m.body FROM messages m WHERE m.project=? AND m.status IN ('acknowledged','expired','failed') AND m.terminal_at<=? AND m.body_digest IS NULL AND NOT EXISTS (SELECT 1 FROM publication_messages pm JOIN publication_messages siblings ON siblings.publication=pm.publication JOIN messages s ON s.id=siblings.message WHERE pm.message=m.id AND (s.status NOT IN ('acknowledged','expired','failed') OR s.terminal_at IS NULL OR s.terminal_at>?))", p.Project, cutoff, cutoff);
            foreach (var row in messages)
                _ = db.Exec("UPDATE messages SET body='',body_digest=? WHERE id=?", Digest(row[1]!), row[0]);
            var publications = db.Query("SELECT p.id,p.body FROM publications p WHERE p.project=? AND p.body_digest IS NULL AND (p.expires<=? OR EXISTS (SELECT 1 FROM publication_messages pm WHERE pm.publication=p.id)) AND NOT EXISTS (SELECT 1 FROM publication_messages pm JOIN messages m ON m.id=pm.message WHERE pm.publication=p.id AND m.body_digest IS NULL)", p.Project, cutoff);
            foreach (var row in publications)
                _ = db.Exec("UPDATE publications SET body='',body_digest=? WHERE id=?", Digest(row[1]!), row[0]);
            if (messages.Count != 0)
                Log(p.Project, "bodies-redacted", null, messages.Count.ToString());
            return messages.Count;
        });
    }
    public string[] Activity(Principal p, long? beforeSequence = null, int limit = 10) {
        Registered(p);
        if (beforeSequence is <= 0 || limit is < 1 or > 10)
            throw new ArgumentOutOfRangeException();
        // Use a project-local ordinal rather than the database-wide sequence;
        // cursors must not reveal activity in other projects.
        return [.. db.Query("WITH project_activity AS (SELECT ROW_NUMBER() OVER (ORDER BY seq) AS ordinal,at,event,COALESCE(message,'') AS message,COALESCE(detail,'') AS detail FROM activity WHERE project=?) SELECT ordinal,at,event,message,detail FROM project_activity WHERE (? IS NULL OR ordinal<CAST(? AS INTEGER)) ORDER BY ordinal DESC LIMIT ?", p.Project, beforeSequence?.ToString(), beforeSequence?.ToString(), limit.ToString()).AsEnumerable().Reverse().Select(r => string.Join(" | ", r))];
    }
    public void Dispose() => db.Dispose();
}
