namespace Pigeon.Core;

// This service's own settings for other machines: its name and where its peer listener runs.
public sealed partial class Mailbox {
    // The first alpha.3 design kept pairings waiting for a check code here; joins with a code replaced it.
    private void CreatePairingSchema() => db.Exec("DROP TABLE IF EXISTS p_pending_peers");

    public string? Setting(string key) => db.Query("SELECT value FROM pigeon_metadata WHERE key=?", "peer." + key).FirstOrDefault()?[0];

    public void SetSetting(string key, string? value) {
        if (value is null)
            _ = db.Exec("DELETE FROM pigeon_metadata WHERE key=?", "peer." + key);
        else
            _ = db.Exec("INSERT OR REPLACE INTO pigeon_metadata(key,value) VALUES(?,?)", "peer." + key, value);
    }
}
