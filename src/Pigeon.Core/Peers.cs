using System.Globalization;
using System.Text.RegularExpressions;

namespace Pigeon.Core;

public sealed record PeerInfo(string Fingerprint, string Name, string? Address, long AddedUnixMs, long? LastContactUnixMs);
// A remote topic (see docs/remote-design.md). Role "offer": a local topic whose second seat belongs to
// a machine that joined it. Role "join": a topic on another machine, which exists here as
// "<offered>@<machine name>". Offered is the listing machine's topic name, the key both services use.
public sealed record ShareInfo(string Topic, string PeerName, string Offered, string Role, bool ShareTool);
// Kind "message" or "leave"; Offered is the listing machine's topic name.
public sealed record OutboxItem(string Id, string PeerFingerprint, string Kind, string Offered, string? MessageId, string? SenderTool, string? Body, long ExpiresUnixMs, int Attempts);

// Topics with a member on another machine. A remote topic's other seat is a remote seat: a reserved
// member ID, "peer:<fingerprint>:<offered topic>", that local callers cannot use. Messages to it wait
// in an outbox for delivery instead of waking anyone.
public sealed partial class Mailbox {
    public const string RemoteSeatPrefix = "peer:";
    // The sender of Pigeon's own notices ("codex @office-laptop joined project-1.").
    public const string NoticeSender = "peer:notice";
    private const string RemoteTool = "remote";
    private const long MaxRetryDelayMilliseconds = 300000;
    private const long DayMilliseconds = 86400000;

    public static bool IsRemoteSeat(string session) => session.StartsWith(RemoteSeatPrefix, StringComparison.Ordinal);
    private static string Seat(string fingerprint, string offered) => $"{RemoteSeatPrefix}{fingerprint}:{offered}";

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex FingerprintPattern();
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex PeerNamePattern();

    private void CreatePeerSchema() {
        foreach (var sql in new[] {
            "CREATE TABLE IF NOT EXISTS p_peers(fingerprint TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE, address TEXT, added INTEGER NOT NULL, last_contact INTEGER)",
            "CREATE TABLE IF NOT EXISTS p_shares(topic TEXT PRIMARY KEY, peer TEXT NOT NULL, offered TEXT NOT NULL, role TEXT NOT NULL, share_tool INTEGER NOT NULL, created INTEGER NOT NULL, UNIQUE(peer,offered,role))",
            "CREATE TABLE IF NOT EXISTS p_outbox(id TEXT PRIMARY KEY, peer TEXT NOT NULL, kind TEXT NOT NULL, offered TEXT NOT NULL, message TEXT, created INTEGER NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, next_at INTEGER NOT NULL, expires INTEGER NOT NULL)",
            "CREATE INDEX IF NOT EXISTS p_outbox_due ON p_outbox(peer,next_at)",
            // remote: listed to other machines while a seat is free. approval: a local joiner needs a code.
            "CREATE TABLE IF NOT EXISTS p_topic_options(topic TEXT PRIMARY KEY, remote INTEGER NOT NULL, approval INTEGER NOT NULL)",
            // A removed machine is told, so its side ends too: its address and a "forget" item are
            // kept for up to a day after the machine itself is forgotten.
            "CREATE TABLE IF NOT EXISTS p_farewells(fingerprint TEXT PRIMARY KEY, address TEXT NOT NULL, expires INTEGER NOT NULL)"
        })
            _ = db.Exec(sql);
    }

    private static void ValidFingerprint(string fingerprint) {
        if (!FingerprintPattern().IsMatch(fingerprint))
            throw new ArgumentException("Invalid peer fingerprint");
    }

    public static bool IsValidPeerName(string name) => PeerNamePattern().IsMatch(name);

    private static void ValidPeerName(string name) {
        if (!PeerNamePattern().IsMatch(name))
            throw new ArgumentException("A machine name uses lowercase letters, digits and hyphens, up to 64 characters.");
    }

    // A host name such as "DESKTOP-AB12" or "Ana's MacBook.local" as a valid machine name.
    public static string TidyName(string? name) {
        var text = (name ?? "").Trim().ToLowerInvariant().Replace("'", "").Replace("’", "");
        if (text.EndsWith(".local", StringComparison.Ordinal))
            text = text[..^6];
        var tidy = Regex.Replace(Regex.Replace(text, "[^a-z0-9]+", "-"), "^-+|-+$", "");
        if (tidy.Length > 64)
            tidy = tidy[..64].TrimEnd('-');
        return tidy.Length == 0 ? "pigeon" : tidy;
    }

    private static PeerInfo ToPeer(string?[] r) => new(r[0]!, r[1]!, r[2], Long(r[3]), r[4] is null ? null : Long(r[4]));
    private const string PeerColumns = "fingerprint,name,address,added,last_contact";

    public PeerInfo AddPeer(string fingerprint, string name, string? address) {
        ValidFingerprint(fingerprint);
        ValidPeerName(name);
        if (address is not null)
            Required(address, "address");
        return db.Transaction(() => AddPeerCore(fingerprint, name, address));
    }

    // A known machine keeps its name; a new one whose name is taken gets a number ("dev-laptop-2").
    private PeerInfo AddPeerCore(string fingerprint, string name, string? address) {
        if (PeerByFingerprint(fingerprint) is { } known) {
            if (address is not null)
                _ = db.Exec("UPDATE p_peers SET address=? WHERE fingerprint=?", address, fingerprint);
            return PeerByFingerprint(fingerprint) ?? known;
        }

        // Known again: a farewell still waiting to be delivered must not end the new connection.
        FarewellDoneCore(fingerprint);
        var unique = name;
        for (var n = 2; PeerByName(unique) is not null; n++)
            unique = $"{(name.Length > 60 ? name[..60] : name)}-{n}";
        _ = db.Exec("INSERT INTO p_peers(fingerprint,name,address,added) VALUES(?,?,?,?)", fingerprint, unique, address, Time());
        return ToPeer(db.Query($"SELECT {PeerColumns} FROM p_peers WHERE fingerprint=?", fingerprint)[0]);
    }

    public PeerInfo[] Peers() => [.. db.Query($"SELECT {PeerColumns} FROM p_peers ORDER BY name").Select(ToPeer)];
    public PeerInfo? PeerByName(string name) => db.Query($"SELECT {PeerColumns} FROM p_peers WHERE name=?", name).Select(ToPeer).FirstOrDefault();
    public PeerInfo? PeerByFingerprint(string fingerprint) => db.Query($"SELECT {PeerColumns} FROM p_peers WHERE fingerprint=?", fingerprint).Select(ToPeer).FirstOrDefault();
    public PeerInfo? PeerByAddress(string address) => db.Query($"SELECT {PeerColumns} FROM p_peers WHERE address=?", address).Select(ToPeer).FirstOrDefault();

    public void SetPeerAddress(string name, string address) {
        Required(address, "address");
        if (db.Exec("UPDATE p_peers SET address=? WHERE name=?", address, name) == 0)
            throw new InvalidOperationException($"No machine is named \"{name}\".");
    }

    // A machine that answered or connected counts as active, and so do its remote seats, which keeps
    // its topics from being deleted as abandoned. An authenticated contact also updates its address.
    public void TouchPeer(string fingerprint, string? address = null) => db.Transaction(() => {
        _ = db.Exec("UPDATE p_peers SET last_contact=? WHERE fingerprint=?", Time(), fingerprint);
        if (address is not null)
            _ = db.Exec("UPDATE p_peers SET address=? WHERE fingerprint=?", address, fingerprint);
        return db.Exec("UPDATE p_sessions SET last_seen=? WHERE session LIKE ?", Time(), Seat(fingerprint, "") + "%");
    });

    // Ends every topic with the machine at once and forgets it. Nothing more is sent to it.
    // Forgets a machine and ends every topic with it. The machine is told (found missing in the
    // two-machine test on 2026-11-07, where it kept the topic and retried for a day): a "forget"
    // item goes to its last address, so its side ends the topics and forgets this machine too.
    public bool RemovePeer(string name) => db.Transaction(() => {
        var peer = PeerByName(name);
        if (peer is null)
            return false;
        RemovePeerCore(peer, topic => $"{name} was removed, so {topic} no longer reaches it.");
        if (peer.Address is not null) {
            var expires = checked(now() + DayMilliseconds);
            _ = db.Exec("INSERT OR REPLACE INTO p_farewells(fingerprint,address,expires) VALUES(?,?,?)", peer.Fingerprint, peer.Address, Number(expires));
            Enqueue(peer.Fingerprint, "forget", "-", null, expires);
        }

        return true;
    });

    private void RemovePeerCore(PeerInfo peer, Func<string, string> notice) {
        foreach (var share in SharesWith(peer.Fingerprint))
            EndShareCore(share, peer.Fingerprint, notice(share.Topic));
        _ = db.Exec("DELETE FROM p_outbox WHERE peer=?", peer.Fingerprint);
        _ = db.Exec("DELETE FROM p_peers WHERE fingerprint=?", peer.Fingerprint);
    }

    // The other machine removed this one: end its topics here and forget it as well.
    public bool ReceiveForget(string fingerprint) => db.Transaction(() => {
        var peer = PeerByFingerprint(fingerprint);
        if (peer is null)
            return false;
        RemovePeerCore(peer, topic => $"{peer.Name} removed this machine, so {topic} no longer reaches it.");
        return true;
    });

    // Removed machines still to be told, as peers to deliver to (name "removed").
    public PeerInfo[] Farewells() => db.Transaction(() => {
        foreach (var gone in db.Query("SELECT fingerprint FROM p_farewells WHERE expires<=?", Time()))
            FarewellDoneCore(gone[0]!);
        return db.Query("SELECT fingerprint,address FROM p_farewells").Select(r => new PeerInfo(r[0]!, "removed", r[1], 0, null)).ToArray();
    });

    public void FarewellDone(string fingerprint) => db.Transaction(() => {
        FarewellDoneCore(fingerprint);
        return 0;
    });

    private void FarewellDoneCore(string fingerprint) {
        if (db.Exec("DELETE FROM p_farewells WHERE fingerprint=?", fingerprint) != 0)
            _ = db.Exec("DELETE FROM p_outbox WHERE peer=? AND kind='forget'", fingerprint);
    }

    private ShareInfo ToShare(string?[] r) => new(r[0]!, PeerByFingerprint(r[1]!)?.Name ?? r[1]!, r[2]!, r[3]!, r[4] == "1");
    private const string ShareColumns = "topic,peer,offered,role,share_tool";
    private ShareInfo[] SharesWith(string fingerprint)
        => [.. db.Query($"SELECT {ShareColumns} FROM p_shares WHERE peer=?", fingerprint).Select(ToShare)];
    public ShareInfo[] Shares() => [.. db.Query($"SELECT {ShareColumns} FROM p_shares ORDER BY topic").Select(ToShare)];
    private (ShareInfo Share, string Fingerprint)? ShareOf(string topic)
        => db.Query($"SELECT {ShareColumns} FROM p_shares WHERE topic=?", topic).Select(r => ((ShareInfo, string)?)(ToShare(r), r[1]!)).FirstOrDefault();

    // Options a session sets when it opens a topic: listed to other machines, or a code for local joiners.
    public (bool Remote, bool Approval) TopicOptions(string topic) {
        var row = db.Query("SELECT remote,approval FROM p_topic_options WHERE topic=?", topic).FirstOrDefault();
        return row is null ? (false, false) : (row[0] == "1", row[1] == "1");
    }

    private void SetTopicOptions(string topic, bool remote, bool approval) {
        var (wasRemote, hadApproval) = TopicOptions(topic);
        if (!remote && !approval && !wasRemote && !hadApproval)
            return;
        _ = db.Exec("INSERT OR REPLACE INTO p_topic_options(topic,remote,approval) VALUES(?,?,?)", topic, remote || wasRemote ? "1" : "0", approval || hadApproval ? "1" : "0");
    }

    // The topics another machine may ask to join: opened with remote, with one local member and a free seat.
    public string[] ListedTopics() => ListedTopicsCore();

    private string[] ListedTopicsCore() => [.. db.Query("SELECT o.topic FROM p_topic_options o WHERE o.remote=1 AND instr(o.topic,'@')=0 AND NOT EXISTS (SELECT 1 FROM p_shares s WHERE s.topic=o.topic) " +
        "AND (SELECT COUNT(*) FROM p_members m WHERE m.topic=o.topic)=1 ORDER BY o.topic").Select(r => r[0]!)];

    public bool IsListed(string topic) => ListedTopicsCore().Contains(topic, StringComparer.Ordinal);

    // The local member of a topic, who answers requests to join it.
    public Member? LocalMemberOf(string topic) => MembersOf(topic).FirstOrDefault(m => !IsRemoteSeat(m.SessionId));

    // The listing side allowed a join: the machine's seat becomes the topic's second member.
    public ShareInfo LinkTopic(string fingerprint, string offered, string? senderTool) {
        Required(offered, "topic");
        return db.Transaction(() => {
            Sweep();
            var peer = PeerByFingerprint(fingerprint) ?? throw new InvalidOperationException("Unknown machine.");
            if (!IsListed(offered))
                throw new InvalidOperationException($"Topic \"{offered}\" is no longer open to other machines.");
            var seat = Seat(fingerprint, offered);
            var tool = SafeTool(senderTool);
            _ = db.Exec("INSERT OR REPLACE INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", seat, tool, Time());
            _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", offered, seat, Time());
            _ = db.Exec("INSERT INTO p_shares(topic,peer,offered,role,share_tool,created) VALUES(?,?,?,'offer',1,?)", offered, fingerprint, offered, Time());
            // A completed join is contact with that machine (it showed "never" in the two-machine test).
            _ = db.Exec("UPDATE p_peers SET last_contact=? WHERE fingerprint=?", Time(), fingerprint);
            // Messages already waiting for a second member go to the machine.
            foreach (var row in db.Query("SELECT id,expires FROM p_messages WHERE topic=? AND recipient IS NULL AND status='queued' AND sender<>?", offered, NoticeSender)) {
                _ = db.Exec("UPDATE p_messages SET recipient=? WHERE id=?", seat, row[0]);
                Enqueue(fingerprint, "message", offered, row[0], Long(row[1]));
            }

            if (LocalMemberOf(offered) is { } local)
                NoticeCore(local.SessionId, offered, $"{tool} @{peer.Name} joined {offered} from another machine.");
            return new ShareInfo(offered, peer.Name, offered, "offer", true);
        });
    }

    // The requesting side was allowed in: the topic appears here as "<offered>@<machine>", with the
    // requesting session already in it.
    public ShareInfo LinkJoin(string fingerprint, string offered, string? listingTool, string session, string toolName) {
        Required(offered, "topic");
        Required(session, "session ID");
        var tool = CanonicalTool(toolName);
        return db.Transaction(() => {
            var peer = PeerByFingerprint(fingerprint) ?? throw new InvalidOperationException("Unknown machine.");
            var topic = offered + "@" + peer.Name;
            if (db.Query("SELECT 1 FROM p_topics WHERE topic=?", topic).Count != 0)
                throw new InvalidOperationException($"Topic \"{topic}\" already exists here.");
            var seat = Seat(fingerprint, offered);
            var remoteTool = SafeTool(listingTool);
            if (ToolOf(session) is null)
                _ = db.Exec("INSERT INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", session, tool, Time());
            _ = db.Exec("INSERT OR REPLACE INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", seat, remoteTool, Time());
            _ = db.Exec("INSERT INTO p_topics(topic,created) VALUES(?,?)", topic, Time());
            _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", topic, seat, Time());
            _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", topic, session, Time());
            _ = db.Exec("INSERT INTO p_shares(topic,peer,offered,role,share_tool,created) VALUES(?,?,?,'join',1,?)", topic, fingerprint, offered, Time());
            // A completed join is contact with that machine (it showed "never" in the two-machine test).
            _ = db.Exec("UPDATE p_peers SET last_contact=? WHERE fingerprint=?", Time(), fingerprint);
            NoticeCore(session, topic, $"You joined {topic}, with {remoteTool} @{peer.Name} on the other machine. Send on {topic}.");
            return new ShareInfo(topic, peer.Name, offered, "join", true);
        });
    }

    // A session that asked to join a topic is known before it is in one, so Pigeon's notices reach it.
    public void EnsureSession(string toolName, string session) {
        Required(session, "session ID");
        var tool = CanonicalTool(toolName);
        var known = ToolOf(session);
        if (known is not null && known != tool)
            throw new InvalidOperationException($"Session {session} already joined using {known}.");
        _ = known is null
            ? db.Exec("INSERT INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", session, tool, Time())
            : db.Exec("UPDATE p_sessions SET last_seen=? WHERE session=?", Time(), session);
    }

    public bool IsMember(string session, string topic) => db.Query("SELECT 1 FROM p_members WHERE topic=? AND session=?", topic, session).Count != 0;
    public int MemberCount(string topic) => MembersOf(topic).Length;

    private static string SafeTool(string? tool) => ToolName(tool) is { Length: > 0 and <= 64 } named && !named.Any(char.IsControl) ? named : RemoteTool;

    // Ends a remote topic on this side. A copy of another machine's topic is deleted; a local topic
    // loses the machine's seat, keeps its local member, and is listed again if it was open.
    private void EndShareCore(ShareInfo share, string fingerprint, string? notice) {
        _ = db.Exec("DELETE FROM p_outbox WHERE peer=? AND offered=?", fingerprint, share.Offered);
        _ = db.Exec("DELETE FROM p_shares WHERE topic=?", share.Topic);
        var seat = Seat(fingerprint, share.Offered);
        if (share.Role == "join") {
            DeleteTopicCore(share.Topic);
        }
        else {
            _ = db.Exec("DELETE FROM p_messages WHERE topic=? AND (recipient=? OR sender=?) AND status IN ('queued','leased')", share.Topic, seat, seat);
            _ = db.Exec("DELETE FROM p_members WHERE topic=? AND session=?", share.Topic, seat);
            if (LocalMemberOf(share.Topic) is { } local && notice is not null)
                NoticeCore(local.SessionId, share.Topic, notice);
            else if (db.Query("SELECT 1 FROM p_members WHERE topic=?", share.Topic).Count == 0)
                DeleteTopicCore(share.Topic);
        }

        _ = db.Exec("DELETE FROM p_sessions WHERE session=? AND NOT EXISTS (SELECT 1 FROM p_members WHERE session=?)", seat, seat);
    }

    // The other machine's member left: the topic ends on this side.
    public bool ReceiveLeave(string fingerprint, string offered) => db.Transaction(() => {
        var row = db.Query($"SELECT {ShareColumns} FROM p_shares WHERE peer=? AND offered=?", fingerprint, offered).FirstOrDefault();
        if (row is null)
            return false;
        var share = ToShare(row);
        EndShareCore(share, fingerprint, $"@{share.PeerName} left {share.Topic}. The topic is open to other machines again.");
        return true;
    });

    // A message from the other machine's member of a remote topic. Returns false when no such topic
    // exists (the sender is told), true when it is stored or was stored before (the same ID is a no-op).
    // lifetime is the time left before the message expires, so clocks may differ between machines.
    public bool ReceiveFromPeer(string fingerprint, string offered, string messageId, string? senderTool, string body, long lifetime) {
        Required(offered, "topic");
        Required(messageId, "message ID", 64);
        Required(body, "message", 16384);
        return db.Transaction(() => {
            Sweep();
            var share = db.Query("SELECT topic FROM p_shares WHERE peer=? AND offered=?", fingerprint, offered).FirstOrDefault();
            if (share is null)
                return false;
            var topic = share[0]!;
            if (db.Query("SELECT 1 FROM p_messages WHERE id=?", messageId).Count != 0)
                return true;
            if (lifetime <= 0)
                return true;
            var sent = int.Parse(db.Query("SELECT COUNT(*) FROM p_messages WHERE topic=? AND created>? AND sender<>?", topic, Number(checked(now() - 3600000)), NoticeSender)[0][0]!, CultureInfo.InvariantCulture);
            if (sent >= maxMessagesPerHour)
                throw new InvalidOperationException($"Topic \"{offered}\" reached its limit of {maxMessagesPerHour} messages per hour; try again later.");
            var seat = Seat(fingerprint, offered);
            if (ToolName(senderTool) is { Length: > 0 and <= 64 } tool && !tool.Any(char.IsControl))
                _ = db.Exec("UPDATE p_sessions SET tool=? WHERE session=?", tool, seat);
            var local = LocalMemberOf(topic);
            var expires = checked(now() + Math.Min(lifetime, DayMilliseconds));
            _ = db.Exec("INSERT INTO p_messages(id,topic,sender,recipient,body,idem,created,expires,status) VALUES(?,?,?,?,?,?,?,?,'queued')", messageId, topic, seat, local?.SessionId, body, "peer:" + messageId, Time(), Number(expires));
            if (local is not null)
                WakeForNewMessage(local.SessionId);
            return true;
        });
    }

    // A message from Pigeon itself to one session, delivered and woken like any other.
    public void Notice(string session, string topic, string text) => db.Transaction(() => {
        NoticeCore(session, topic, text);
        return 0;
    });

    private void NoticeCore(string session, string topic, string text) {
        if (ToolOf(NoticeSender) is null)
            _ = db.Exec("INSERT INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", NoticeSender, "pigeon", Time());
        var id = Guid.NewGuid().ToString("N");
        _ = db.Exec("INSERT INTO p_messages(id,topic,sender,recipient,body,idem,created,expires,status) VALUES(?,?,?,?,?,?,?,?,'queued')", id, topic, NoticeSender, session, text, id, Time(), Number(checked(now() + DayMilliseconds)));
        WakeForNewMessage(session);
    }

    private void Enqueue(string fingerprint, string kind, string offered, string? messageId, long expires)
        => db.Exec("INSERT INTO p_outbox(id,peer,kind,offered,message,created,next_at,expires) VALUES(?,?,?,?,?,?,?,?)", Guid.NewGuid().ToString("N"), fingerprint, kind, offered, messageId, Time(), Time(), Number(expires));

    // Called from Send when the topic's other member is a remote seat.
    private void EnqueueForPeer(string topic, string seat, string messageId, long expires) {
        var share = db.Query("SELECT peer,offered FROM p_shares WHERE topic=?", topic).FirstOrDefault();
        if (share is not null && seat == Seat(share[0]!, share[1]!))
            Enqueue(share[0]!, "message", share[1]!, messageId, expires);
    }

    // Called from Leave: a local member leaving a remote topic ends it on both sides.
    private bool LeaveRemote(string session, string topic) {
        var found = ShareOf(topic);
        if (found is null || IsRemoteSeat(session))
            return false;
        var (share, fingerprint) = found.Value;
        EndShareCore(share, fingerprint, null);
        Enqueue(fingerprint, "leave", share.Offered, null, checked(now() + DayMilliseconds));
        return true;
    }

    // Items ready to send to one machine, oldest first. Expired ones are dropped, and their messages expire.
    public OutboxItem[] DueOutbox(string fingerprint, int max = 50) => db.Transaction(() => {
        Sweep();
        _ = db.Exec("DELETE FROM p_outbox WHERE expires<=?", Time());
        return db.Query(
            "SELECT o.id,o.peer,o.kind,o.offered,o.message,s.tool,m.body,o.expires,o.attempts FROM p_outbox o " +
            "LEFT JOIN p_messages m ON m.id=o.message LEFT JOIN p_sessions s ON s.session=m.sender " +
            "WHERE o.peer=? AND o.next_at<=? AND (o.message IS NULL OR m.status='queued') ORDER BY o.created,o.rowid LIMIT ?", fingerprint, Time(), Number(max))
            .Select(r => new OutboxItem(r[0]!, r[1]!, r[2]!, r[3]!, r[4], r[5], r[6], Long(r[7]), int.Parse(r[8]!, CultureInfo.InvariantCulture))).ToArray();
    });

    // The machine acknowledged the item: a delivered message is done on this side.
    public void OutboxDelivered(string id) => db.Transaction(() => {
        var message = db.Query("SELECT message FROM p_outbox WHERE id=?", id).FirstOrDefault()?[0];
        if (message is not null)
            _ = db.Exec("UPDATE p_messages SET status='acknowledged',terminal_at=?,attempts=attempts+1 WHERE id=? AND status='queued'", Time(), message);
        return db.Exec("DELETE FROM p_outbox WHERE id=?", id);
    });

    // Delivery failed or the machine was unreachable: retry later, backing off up to five minutes.
    public void OutboxFailed(string id) => db.Exec(
        "UPDATE p_outbox SET attempts=attempts+1,next_at=?+MIN(?,5000*(1<<MIN(attempts,10))) WHERE id=?", Time(), Number(MaxRetryDelayMilliseconds), id);

    // The machine does not know the topic: the item can never be delivered.
    public void OutboxRejected(string id) => db.Transaction(() => {
        var message = db.Query("SELECT message FROM p_outbox WHERE id=?", id).FirstOrDefault()?[0];
        if (message is not null)
            _ = db.Exec("UPDATE p_messages SET status='failed',terminal_at=? WHERE id=? AND status='queued'", Time(), message);
        return db.Exec("DELETE FROM p_outbox WHERE id=?", id);
    });

    public int OutboxCount(string fingerprint) => int.Parse(db.Query("SELECT COUNT(*) FROM p_outbox WHERE peer=?", fingerprint)[0][0]!, CultureInfo.InvariantCulture);
}
