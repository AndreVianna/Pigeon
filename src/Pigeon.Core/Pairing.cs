using System.Globalization;

namespace Pigeon.Core;

public sealed record PendingPeer(string Fingerprint, string Name, string? Address, string Check, long ExpiresUnixMs);

// Pairings an invitation accepted that wait for the inviter to confirm the check code
// (`pigeon peer confirm`), and this service's own peer settings.
public sealed partial class Mailbox {
    private void CreatePairingSchema()
        => db.Exec("CREATE TABLE IF NOT EXISTS p_pending_peers(fingerprint TEXT PRIMARY KEY, name TEXT NOT NULL, address TEXT, check_code TEXT NOT NULL, expires INTEGER NOT NULL)");

    public string? Setting(string key) => db.Query("SELECT value FROM pigeon_metadata WHERE key=?", "peer." + key).FirstOrDefault()?[0];

    public void SetSetting(string key, string? value) {
        if (value is null)
            _ = db.Exec("DELETE FROM pigeon_metadata WHERE key=?", "peer." + key);
        else
            _ = db.Exec("INSERT OR REPLACE INTO pigeon_metadata(key,value) VALUES(?,?)", "peer." + key, value);
    }

    public void AddPendingPeer(string fingerprint, string name, string? address, string check, long expires) {
        ValidFingerprint(fingerprint);
        ValidPeerName(name);
        _ = db.Exec("INSERT OR REPLACE INTO p_pending_peers(fingerprint,name,address,check_code,expires) VALUES(?,?,?,?,?)", fingerprint, name, address, check, Number(expires));
    }

    public PendingPeer[] PendingPeers() => db.Transaction(PendingPeersCore);

    private PendingPeer[] PendingPeersCore() {
        _ = db.Exec("DELETE FROM p_pending_peers WHERE expires<=?", Time());
        return [.. db.Query("SELECT fingerprint,name,address,check_code,expires FROM p_pending_peers ORDER BY expires")
            .Select(r => new PendingPeer(r[0]!, r[1]!, r[2], r[3]!, long.Parse(r[4]!, CultureInfo.InvariantCulture)))];
    }

    // The inviter saw the same check code as the acceptor: the pending peer becomes a paired peer,
    // named as the acceptor proposed unless a name is given here.
    public PeerInfo ConfirmPendingPeer(string check, string? name) => db.Transaction(() => {
        var pending = PendingPeersCore().FirstOrDefault(p => p.Check == check)
            ?? throw new InvalidOperationException("No pairing waits for that check code; it may have expired. Send a new invitation.");
        if (name is not null)
            ValidPeerName(name);
        var peer = AddPeerCore(pending.Fingerprint, name ?? pending.Name, pending.Address);
        _ = db.Exec("DELETE FROM p_pending_peers WHERE fingerprint=?", pending.Fingerprint);
        return peer;
    });
}
