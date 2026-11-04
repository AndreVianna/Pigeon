using System.Globalization;
using System.Text.RegularExpressions;

namespace Pigeon.Core;

public sealed record PeerInfo(string Fingerprint, string Name, string? Address, long AddedUnixMs, long? LastContactUnixMs);
// Role "offer": this service shares one of its topics with the peer. Role "join": the peer shared one
// of its topics, which exists here as "<offered>@<peer name>". Offered is the offerer's topic name,
// the key both services use for the topic on the wire.
public sealed record ShareInfo(string Topic, string PeerName, string Offered, string Role, bool ShareTool);
// Kind "offer", "unshare" or "message"; Topic is the offered topic name.
public sealed record OutboxItem(string Id, string PeerFingerprint, string Kind, string Offered, string? MessageId, string? SenderTool, string? Body, long ExpiresUnixMs, int Attempts);

// Topics shared with paired services on other machines (see docs/remote-design.md). A shared topic's
// other seat is a remote seat: a reserved member ID, "peer:<fingerprint>:<offered topic>", that local
// callers cannot use. Messages to it wait in an outbox for delivery instead of waking anyone.
public sealed partial class Mailbox {
    public const string RemoteSeatPrefix = "peer:";
    private const string RemoteTool = "remote";
    private const long MaxRetryDelayMilliseconds = 300000;

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
            "CREATE INDEX IF NOT EXISTS p_outbox_due ON p_outbox(peer,next_at)"
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
            throw new ArgumentException("A peer name uses lowercase letters, digits and hyphens, up to 64 characters.");
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

    private PeerInfo AddPeerCore(string fingerprint, string name, string? address) {
        if (db.Query("SELECT 1 FROM p_peers WHERE fingerprint=?", fingerprint).Count != 0)
            throw new InvalidOperationException("This service is already paired.");
        if (db.Query("SELECT 1 FROM p_peers WHERE name=?", name).Count != 0)
            throw new InvalidOperationException($"A peer named \"{name}\" already exists; choose another name.");
        _ = db.Exec("INSERT INTO p_peers(fingerprint,name,address,added) VALUES(?,?,?,?)", fingerprint, name, address, Time());
        return ToPeer(db.Query($"SELECT {PeerColumns} FROM p_peers WHERE fingerprint=?", fingerprint)[0]);
    }

    public PeerInfo[] Peers() => [.. db.Query($"SELECT {PeerColumns} FROM p_peers ORDER BY name").Select(ToPeer)];
    public PeerInfo? PeerByName(string name) => db.Query($"SELECT {PeerColumns} FROM p_peers WHERE name=?", name).Select(ToPeer).FirstOrDefault();
    public PeerInfo? PeerByFingerprint(string fingerprint) => db.Query($"SELECT {PeerColumns} FROM p_peers WHERE fingerprint=?", fingerprint).Select(ToPeer).FirstOrDefault();

    public void SetPeerAddress(string name, string address) {
        Required(address, "address");
        if (db.Exec("UPDATE p_peers SET address=? WHERE name=?", address, name) == 0)
            throw new InvalidOperationException($"No peer is named \"{name}\".");
    }

    // A peer that answered or connected counts as active, and so do its remote seats, which keeps
    // its shared topics from being deleted as abandoned.
    public void TouchPeer(string fingerprint) => db.Transaction(() => {
        _ = db.Exec("UPDATE p_peers SET last_contact=? WHERE fingerprint=?", Time(), fingerprint);
        return db.Exec("UPDATE p_sessions SET last_seen=? WHERE session LIKE ?", Time(), Seat(fingerprint, "") + "%");
    });

    // Ends every share with the peer at once: topics it shared here are deleted, and topics shared
    // with it lose its seat. Nothing more is sent to it.
    public bool RemovePeer(string name) => db.Transaction(() => {
        var peer = PeerByName(name);
        if (peer is null)
            return false;
        foreach (var share in SharesWith(peer.Fingerprint))
            EndShareCore(share, peer.Fingerprint);
        _ = db.Exec("DELETE FROM p_outbox WHERE peer=?", peer.Fingerprint);
        _ = db.Exec("DELETE FROM p_peers WHERE fingerprint=?", peer.Fingerprint);
        return true;
    });

    private ShareInfo ToShare(string?[] r) => new(r[0]!, PeerByFingerprint(r[1]!)?.Name ?? r[1]!, r[2]!, r[3]!, r[4] == "1");
    private ShareInfo[] SharesWith(string fingerprint)
        => [.. db.Query("SELECT topic,peer,offered,role,share_tool FROM p_shares WHERE peer=?", fingerprint).Select(ToShare)];
    public ShareInfo[] Shares() => [.. db.Query("SELECT topic,peer,offered,role,share_tool FROM p_shares ORDER BY topic").Select(ToShare)];

    // Offers a local topic to a peer, whose seat becomes the topic's second member. The local member's
    // tool name goes with the offer unless shareTool is false; its session ID never does.
    public ShareInfo Share(string topic, string peerName, bool shareTool = true) {
        Required(topic, "topic");
        return db.Transaction(() => {
            Sweep();
            var peer = PeerByName(peerName) ?? throw new InvalidOperationException($"No peer is named \"{peerName}\".");
            if (topic.Contains('@'))
                throw new InvalidOperationException("A topic shared by a peer cannot be shared again.");
            if (db.Query("SELECT 1 FROM p_topics WHERE topic=?", topic).Count == 0)
                throw new InvalidOperationException($"Topic \"{topic}\" does not exist; join it first.");
            if (db.Query("SELECT 1 FROM p_shares WHERE topic=?", topic).Count != 0)
                throw new InvalidOperationException($"Topic \"{topic}\" is already shared.");
            var members = MembersOf(topic);
            if (members.Length >= 2)
                throw new InvalidOperationException($"Topic \"{topic}\" already has two members; share a topic with one.");
            var seat = Seat(peer.Fingerprint, topic);
            _ = db.Exec("INSERT OR REPLACE INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", seat, RemoteTool, Time());
            _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", topic, seat, Time());
            _ = db.Exec("INSERT INTO p_shares(topic,peer,offered,role,share_tool,created) VALUES(?,?,?,'offer',?,?)", topic, peer.Fingerprint, topic, shareTool ? "1" : "0", Time());
            Enqueue(peer.Fingerprint, "offer", topic, null, checked(now() + 86400000));
            // Messages already waiting for a second member go to the peer.
            foreach (var row in db.Query("SELECT id,expires FROM p_messages WHERE topic=? AND recipient IS NULL AND status='queued'", topic)) {
                _ = db.Exec("UPDATE p_messages SET recipient=? WHERE id=?", seat, row[0]);
                Enqueue(peer.Fingerprint, "message", topic, row[0], Long(row[1]));
            }

            return new ShareInfo(topic, peer.Name, topic, "offer", shareTool);
        });
    }

    // Stops sharing a topic this service offered; the peer is told, and its copy of the topic ends.
    public bool Unshare(string topic) => db.Transaction(() => {
        var row = db.Query("SELECT topic,peer,offered,role,share_tool FROM p_shares WHERE topic=? AND role='offer'", topic).FirstOrDefault();
        if (row is null)
            return false;
        var share = ToShare(row);
        EndShareCore(share, row[1]!);
        Enqueue(row[1]!, "unshare", share.Offered, null, checked(now() + 86400000));
        return true;
    });

    private void EndShareCore(ShareInfo share, string fingerprint) {
        _ = db.Exec("DELETE FROM p_outbox WHERE peer=? AND offered=?", fingerprint, share.Offered);
        _ = db.Exec("DELETE FROM p_shares WHERE topic=?", share.Topic);
        if (share.Role == "join") {
            DeleteTopicCore(share.Topic);
        }
        else {
            var seat = Seat(fingerprint, share.Offered);
            _ = db.Exec("DELETE FROM p_messages WHERE topic=? AND recipient=? AND status IN ('queued','leased')", share.Topic, seat);
            _ = db.Exec("DELETE FROM p_members WHERE topic=? AND session=?", share.Topic, seat);
            if (db.Query("SELECT 1 FROM p_members WHERE topic=?", share.Topic).Count == 0)
                DeleteTopicCore(share.Topic);
        }

        _ = db.Exec("DELETE FROM p_sessions WHERE session=? AND NOT EXISTS (SELECT 1 FROM p_members WHERE session=?)", Seat(fingerprint, share.Offered), Seat(fingerprint, share.Offered));
    }

    // A paired peer offered one of its topics; it appears here as "<offered>@<peer name>", which a
    // local session can then join. Offering again changes nothing.
    public ShareInfo AcceptOffer(string fingerprint, string offered, string? senderTool) {
        Required(offered, "topic");
        return db.Transaction(() => {
            var peer = PeerByFingerprint(fingerprint) ?? throw new InvalidOperationException("Unknown peer.");
            if (offered.Contains('@'))
                throw new ArgumentException("Invalid topic");
            var existing = db.Query("SELECT topic,peer,offered,role,share_tool FROM p_shares WHERE peer=? AND offered=? AND role='join'", fingerprint, offered).FirstOrDefault();
            if (existing is not null)
                return ToShare(existing);
            var topic = offered + "@" + peer.Name;
            if (db.Query("SELECT 1 FROM p_topics WHERE topic=?", topic).Count != 0)
                throw new InvalidOperationException($"Topic \"{topic}\" already exists here.");
            var seat = Seat(fingerprint, offered);
            var tool = ToolName(senderTool) is { Length: > 0 and <= 64 } named && !named.Any(char.IsControl) ? named : RemoteTool;
            _ = db.Exec("INSERT OR REPLACE INTO p_sessions(session,tool,last_seen) VALUES(?,?,?)", seat, tool, Time());
            _ = db.Exec("INSERT INTO p_topics(topic,created) VALUES(?,?)", topic, Time());
            _ = db.Exec("INSERT INTO p_members(topic,session,joined) VALUES(?,?,?)", topic, seat, Time());
            _ = db.Exec("INSERT INTO p_shares(topic,peer,offered,role,share_tool,created) VALUES(?,?,?,'join',1,?)", topic, fingerprint, offered, Time());
            return new ShareInfo(topic, peer.Name, offered, "join", true);
        });
    }

    // The peer stopped sharing one of its topics; the local copy ends.
    public bool ReceiveUnshare(string fingerprint, string offered) => db.Transaction(() => {
        var row = db.Query("SELECT topic,peer,offered,role,share_tool FROM p_shares WHERE peer=? AND offered=? AND role='join'", fingerprint, offered).FirstOrDefault();
        if (row is null)
            return false;
        EndShareCore(ToShare(row), fingerprint);
        return true;
    });

    // A message from the peer's member of a shared topic. Returns false when no such share exists
    // (the peer is told), true when it is stored or was stored before (the same ID twice is a no-op).
    public bool ReceiveFromPeer(string fingerprint, string offered, string messageId, string? senderTool, string body, long expires) {
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
            if (expires <= now())
                return true;
            var sent = int.Parse(db.Query("SELECT COUNT(*) FROM p_messages WHERE topic=? AND created>?", topic, Number(checked(now() - 3600000)))[0][0]!, CultureInfo.InvariantCulture);
            if (sent >= maxMessagesPerHour)
                throw new InvalidOperationException($"Topic \"{offered}\" reached its limit of {maxMessagesPerHour} messages per hour; try again later.");
            var seat = Seat(fingerprint, offered);
            if (ToolName(senderTool) is { Length: > 0 and <= 64 } tool && !tool.Any(char.IsControl))
                _ = db.Exec("UPDATE p_sessions SET tool=? WHERE session=?", tool, seat);
            var local = MembersOf(topic).FirstOrDefault(m => !IsRemoteSeat(m.SessionId));
            _ = db.Exec("INSERT INTO p_messages(id,topic,sender,recipient,body,idem,created,expires,status) VALUES(?,?,?,?,?,?,?,?,'queued')", messageId, topic, seat, local?.SessionId, body, "peer:" + messageId, Time(), Number(expires));
            if (local is not null)
                WakeForNewMessage(local.SessionId);
            return true;
        });
    }

    private void Enqueue(string fingerprint, string kind, string offered, string? messageId, long expires)
        => db.Exec("INSERT INTO p_outbox(id,peer,kind,offered,message,created,next_at,expires) VALUES(?,?,?,?,?,?,?,?)", Guid.NewGuid().ToString("N"), fingerprint, kind, offered, messageId, Time(), Time(), Number(expires));

    // Called from Send when the topic's other member is a remote seat.
    private void EnqueueForPeer(string topic, string seat, string messageId, long expires) {
        var share = db.Query("SELECT peer,offered FROM p_shares WHERE topic=?", topic).FirstOrDefault();
        if (share is not null && seat == Seat(share[0]!, share[1]!))
            Enqueue(share[0]!, "message", share[1]!, messageId, expires);
    }

    // Items ready to send to one peer, oldest first. Expired ones are dropped, and their messages expire.
    public OutboxItem[] DueOutbox(string fingerprint, int max = 50) => db.Transaction(() => {
        Sweep();
        _ = db.Exec("DELETE FROM p_outbox WHERE expires<=?", Time());
        return db.Query(
            "SELECT o.id,o.peer,o.kind,o.offered,o.message,s.tool,m.body,o.expires,o.attempts,sh.share_tool FROM p_outbox o " +
            "LEFT JOIN p_messages m ON m.id=o.message LEFT JOIN p_sessions s ON s.session=m.sender " +
            "LEFT JOIN p_shares sh ON sh.peer=o.peer AND sh.offered=o.offered " +
            "WHERE o.peer=? AND o.next_at<=? AND (o.message IS NULL OR m.status='queued') ORDER BY o.created,o.rowid LIMIT ?", fingerprint, Time(), Number(max))
            .Select(r => new OutboxItem(r[0]!, r[1]!, r[2]!, r[3]!, r[4], r[9] == "0" ? null : r[5] ?? LocalToolOf(r[3]!), r[6], Long(r[7]), int.Parse(r[8]!, CultureInfo.InvariantCulture))).ToArray();
    });

    // The tool name sent with an offer: the topic's local member, if it has one.
    private string? LocalToolOf(string topic) => MembersOf(topic).FirstOrDefault(m => !IsRemoteSeat(m.SessionId))?.Tool;

    // The peer acknowledged the item: a delivered message is done on this side.
    public void OutboxDelivered(string id) => db.Transaction(() => {
        var message = db.Query("SELECT message FROM p_outbox WHERE id=?", id).FirstOrDefault()?[0];
        if (message is not null)
            _ = db.Exec("UPDATE p_messages SET status='acknowledged',terminal_at=?,attempts=attempts+1 WHERE id=? AND status='queued'", Time(), message);
        return db.Exec("DELETE FROM p_outbox WHERE id=?", id);
    });

    // Delivery failed or the peer was unreachable: retry later, backing off up to five minutes.
    public void OutboxFailed(string id) => db.Exec(
        "UPDATE p_outbox SET attempts=attempts+1,next_at=?+MIN(?,5000*(1<<MIN(attempts,10))) WHERE id=?", Time(), Number(MaxRetryDelayMilliseconds), id);

    // The peer does not know the share: the item can never be delivered.
    public void OutboxRejected(string id) => db.Transaction(() => {
        var message = db.Query("SELECT message FROM p_outbox WHERE id=?", id).FirstOrDefault()?[0];
        if (message is not null)
            _ = db.Exec("UPDATE p_messages SET status='failed',terminal_at=? WHERE id=? AND status='queued'", Time(), message);
        return db.Exec("DELETE FROM p_outbox WHERE id=?", id);
    });

    public int OutboxCount(string fingerprint) => int.Parse(db.Query("SELECT COUNT(*) FROM p_outbox WHERE peer=?", fingerprint)[0][0]!, CultureInfo.InvariantCulture);
}
