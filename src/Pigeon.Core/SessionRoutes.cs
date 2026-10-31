using System.Security.Cryptography;
using System.Text;

namespace Pigeon.Core;

// Agent is an internal storage key. Tool and SessionId are the public routing identity.
public sealed record SessionRoute(string Tool, string SessionId, string Agent, string[] Topics, bool RuntimeVerified);
public sealed record SessionMember(string Tool, string SessionId);
public sealed record LegacySessionCandidate(string Agent, string[] Topics, int Queued, int Leased);

public sealed partial class Mailbox {
    private static string CanonicalTool(string tool) {
        Required(tool, "tool");
        var name = tool.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        return name == "claude" ? "claude-code" : name;
    }

    // Called within a transaction. An ambiguous legacy pair is never silently merged.
    private string? ResolveSessionCore(string project, string tool, string session, bool create) {
        var route = db.Query("SELECT agent FROM session_routes WHERE project=? AND tool=? AND session=?", project, tool, session);
        if (route.Count != 0) return route[0][0]!;
        var legacy = db.Query("SELECT agent FROM agents WHERE project=? AND session=? AND lower(replace(replace(runtime,'_','-'),' ','-')) IN (?,?) AND agent<>'__pigeon__' ORDER BY agent", project, session, tool, tool == "claude-code" ? "claude" : tool);
        if (legacy.Count > 1) throw new InvalidOperationException("Ambiguous legacy tool and session; resolve duplicate identities before restoring");
        if (legacy.Count == 1) {
            var agent = legacy[0][0]!;
            if (db.Query("SELECT 1 FROM session_routes WHERE project=? AND agent=?", project, agent).Count != 0)
                throw new InvalidOperationException("Legacy identity is already associated with another session route");
            _ = db.Exec("INSERT INTO session_routes(project,tool,session,agent) VALUES(?,?,?,?)", project, tool, session, agent);
            return agent;
        }
        if (!create) return null;
        // Length-prefix the components so distinct pairs cannot have the same input.
        var raw = tool.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + tool + session;
        var key = "session-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        if (db.Query("SELECT 1 FROM agents WHERE project=? AND agent=?", project, key).Count != 0)
            throw new InvalidOperationException("Session route storage key is already in use");
        RegisterCore(project, key, tool, session, "unknown");
        _ = db.Exec("INSERT INTO session_routes(project,tool,session,agent) VALUES(?,?,?,?)", project, tool, session, key);
        return key;
    }

    private SessionRoute Route(string project, string tool, string session, string agent) =>
        new(tool, session, agent, [.. db.Query("SELECT topic FROM subscriptions WHERE project=? AND agent=? ORDER BY topic", project, agent).Select(row => row[0]!)], false);

    public SessionRoute JoinSession(string project, string toolName, string session, string topic) {
        Required(project, "project"); Required(session, "session"); Required(topic, "topic");
        var tool = CanonicalTool(toolName);
        return db.Transaction(() => {
            if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", project, topic).Count == 0)
                throw new InvalidOperationException("Unknown topic");
            var agent = ResolveSessionCore(project, tool, session, true)!;
            if (db.Exec("INSERT OR IGNORE INTO subscriptions(project,topic,agent) VALUES(?,?,?)", project, topic, agent) != 0)
                Log(project, "subscribed", null, agent + " @ " + topic);
            return Route(project, tool, session, agent);
        });
    }

    public SessionRoute? RestoreSession(string project, string toolName, string session) {
        Required(project, "project"); Required(session, "session");
        var tool = CanonicalTool(toolName);
        return db.Transaction(() => {
            var agent = ResolveSessionCore(project, tool, session, false);
            return agent is null ? null : Route(project, tool, session, agent);
        });
    }

    public SessionMember[] TopicSessions(string project, string topic) {
        Required(project, "project"); Required(topic, "topic");
        if (db.Query("SELECT 1 FROM topics WHERE project=? AND topic=?", project, topic).Count == 0)
            throw new InvalidOperationException("Unknown topic");
        return [.. db.Query("SELECT a.runtime,a.session FROM subscriptions s JOIN agents a ON a.project=s.project AND a.agent=s.agent WHERE s.project=? AND s.topic=? ORDER BY a.runtime,a.session", project, topic).Select(row => new SessionMember(CanonicalTool(row[0]!), row[1]!))];
    }

    public LegacySessionCandidate[] LegacyCandidates(string project, string toolName, string session) {
        Required(project, "project"); Required(session, "session");
        var tool = CanonicalTool(toolName);
        var equivalent = tool == "claude-code" ? "claude" : tool;
        return db.Transaction(() => {
            Sweep(project);
            return db.Query("SELECT agent FROM agents WHERE project=? AND session=? AND lower(replace(replace(runtime,'_','-'),' ','-')) IN (?,?) AND agent<>'__pigeon__' ORDER BY agent", project, session, tool, equivalent).Select(row => {
                var agent = row[0]!;
                var topics = db.Query("SELECT topic FROM subscriptions WHERE project=? AND agent=? ORDER BY topic", project, agent).Select(t => t[0]!).ToArray();
                var pending = PendingCounts(new Principal(project, agent));
                return new LegacySessionCandidate(agent, topics, pending.Queued, pending.Leased);
            }).ToArray();
        });
    }

    // Explicit operator choice; no message, subscription, or history is moved or merged.
    public SessionRoute AdoptLegacySession(string project, string toolName, string session, string legacyAgent) {
        Required(project, "project"); Required(session, "session"); Required(legacyAgent, "agent");
        var tool = CanonicalTool(toolName);
        return db.Transaction(() => {
            var row = db.Query("SELECT runtime,session FROM agents WHERE project=? AND agent=?", project, legacyAgent).FirstOrDefault();
            if (row is null || row[1] != session || CanonicalTool(row[0]!) != tool || legacyAgent == "__pigeon__")
                throw new InvalidOperationException("Selected legacy identity does not match this tool and session");
            var existing = db.Query("SELECT agent FROM session_routes WHERE project=? AND tool=? AND session=?", project, tool, session).FirstOrDefault();
            if (existing is not null && existing[0] != legacyAgent)
                throw new InvalidOperationException("This session route is already associated with another identity");
            if (db.Query("SELECT 1 FROM session_routes WHERE project=? AND agent=? AND (tool<>? OR session<>?)", project, legacyAgent, tool, session).Count != 0)
                throw new InvalidOperationException("Selected legacy identity is already associated with another route");
            if (existing is null) {
                _ = db.Exec("INSERT INTO session_routes(project,tool,session,agent) VALUES(?,?,?,?)", project, tool, session, legacyAgent);
                Log(project, "legacy-route-selected", null, legacyAgent);
            }
            return Route(project, tool, session, legacyAgent);
        });
    }
}
