using System.Globalization;

namespace Pigeon.Core;

public sealed record WakeClaim(string Generation, int Attempt);
public sealed record WakeConfirmation(bool Accepted, int Queued, int Leased);
public sealed record WakeAttempt(string Generation, int Number, long AttemptedAtUnixMs, string Mode, string Outcome);
public sealed record WakeStatus(string State, string? Generation, int Attempts, long? NextAttemptUnixMs, string? LastOutcome, long? ConfirmedAtUnixMs, int Queued, int Leased, WakeAttempt[] History);
public sealed record RecipientWakeStatus(string Agent, WakeStatus Wake, string? Tool = null, string? SessionId = null);

public sealed partial class Mailbox {
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string NewGeneration() => Guid.NewGuid().ToString("N");

    // Called inside the same transaction that creates a delivery. Existing
    // unprocessed work shares a single generation even during a burst.
    private void EnsureWake(string project, string agent) {
        var existing = db.Query("SELECT state FROM wake_state WHERE project=? AND agent=?", project, agent).FirstOrDefault();
        if (existing is null) {
            _ = db.Exec("INSERT INTO wake_state(project,agent,generation,state,attempts,next_at,updated) VALUES(?,?,?,'ready',0,?,?)", project, agent, NewGeneration(), Time(), Time());
        }
        else if (existing[0] == "completed") {
            _ = db.Exec("UPDATE wake_state SET generation=?,state='ready',attempts=0,next_at=?,last_outcome=NULL,confirmed_at=NULL,updated=? WHERE project=? AND agent=?", NewGeneration(), Time(), Time(), project, agent);
        }
    }

    private (int Queued, int Leased) PendingCounts(Principal p) {
        var rows = db.Query("SELECT status,COUNT(*) FROM messages WHERE project=? AND recipient=? AND status IN ('queued','leased') GROUP BY status", p.Project, p.Agent)
            .ToDictionary(row => row[0]!, row => int.Parse(row[1]!, CultureInfo.InvariantCulture));
        return (rows.GetValueOrDefault("queued"), rows.GetValueOrDefault("leased"));
    }

    private void CompleteIfEmpty(Principal p, int queued, int leased) {
        if (queued == 0 && leased == 0)
            _ = db.Exec("UPDATE wake_state SET state='completed',next_at=0,updated=? WHERE project=? AND agent=? AND state<>'completed'", Time(), p.Project, p.Agent);
    }

    private bool MarkUnknownTransport(Principal p, string generation) {
        var changed = db.Exec("UPDATE wake_attempts SET outcome='transport-unknown' WHERE project=? AND agent=? AND generation=? AND outcome='dispatching'", p.Project, p.Agent, generation);
        if (changed > 0) {
            _ = db.Exec("UPDATE wake_state SET last_outcome='transport-unknown',updated=? WHERE project=? AND agent=?", Time(), p.Project, p.Agent);
            Log(p.Project, "wake-transport-unknown", null, p.Agent);
        }
        return changed > 0;
    }

    // A message acknowledgement is progress; it is not itself a wake confirmation.
    private void WakeDeliveryAcknowledged(Principal p) {
        var (queued, leased) = PendingCounts(p);
        CompleteIfEmpty(p, queued, leased);
        if (queued + leased > 0)
            _ = db.Exec("UPDATE wake_state SET attempts=0,updated=? WHERE project=? AND agent=? AND state='confirmed'", Time(), p.Project, p.Agent);
    }

    public WakeClaim? ClaimWake(Principal p, string mode, string? sessionId = null) => db.Transaction(() => {
        Registered(p);
        if (mode is not ("codex-queue" or "claude-channel" or "claude-monitor" or "claude-hook")) throw new ArgumentException("Invalid wake mode");
        var binding = db.Query("SELECT runtime,session FROM agents WHERE project=? AND agent=?", p.Project, p.Agent)[0];
        var runtime = binding[0]!.Replace('_', '-');
        if (mode is "claude-monitor" or "claude-hook" && (string.IsNullOrWhiteSpace(sessionId) || binding[1] != sessionId)) throw new InvalidOperationException("Monitor session does not match binding");
        if ((mode == "codex-queue" && !runtime.Equals("codex", StringComparison.OrdinalIgnoreCase)) ||
            (mode is "claude-channel" or "claude-monitor" or "claude-hook" && !runtime.Equals("claude-code", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Wake mode does not match runtime");
        Sweep(p.Project);
        var (queued, leased) = PendingCounts(p);
        CompleteIfEmpty(p, queued, leased);
        if (queued == 0 || db.Query("SELECT 1 FROM projects WHERE project=? AND paused=1", p.Project).Count != 0) return null;
        EnsureWake(p.Project, p.Agent); // Backfill older databases with queued deliveries.
        var row = db.Query("SELECT generation,state,attempts,next_at FROM wake_state WHERE project=? AND agent=?", p.Project, p.Agent)[0];
        var generation = row[0]!;
        var state = row[1]!;
        var attempts = int.Parse(row[2]!, CultureInfo.InvariantCulture);
        var next = long.Parse(row[3]!, CultureInfo.InvariantCulture);
        if (state == "exhausted" || next > now()) return null;
        _ = MarkUnknownTransport(p, generation);
        if (attempts >= maxWakeAttempts) {
            _ = db.Exec("UPDATE wake_state SET state='exhausted',updated=? WHERE project=? AND agent=?", Time(), p.Project, p.Agent);
            Log(p.Project, "wake-exhausted", null, p.Agent);
            return null;
        }
        // A confirmed but unfinished wake must get a new generation; an old
        // delayed prompt must never drain a later batch.
        if (state == "confirmed") generation = NewGeneration();
        attempts++;
        _ = db.Exec("UPDATE wake_state SET generation=?,state='dispatching',attempts=?,next_at=?,confirmed_at=NULL,updated=? WHERE project=? AND agent=?", generation, Number(attempts), Number(checked(now() + wakeRetryMilliseconds)), Time(), p.Project, p.Agent);
        _ = db.Exec("INSERT INTO wake_attempts(project,agent,generation,number,attempted_at,mode,outcome) VALUES(?,?,?,?,?,?,'dispatching')", p.Project, p.Agent, generation, Number(attempts), Time(), mode);
        Log(p.Project, "wake-attempted", null, p.Agent + " " + mode);
        return new WakeClaim(generation, attempts);
    });

    public bool RecordWakeResult(Principal p, string generation, int attempt, string outcome) => db.Transaction(() => {
        Registered(p);
        Required(generation, "generation");
        if (attempt < 1 || outcome is not ("transport-accepted" or "transport-written" or "transport-failed")) throw new ArgumentException("Invalid wake result");
        var updated = db.Exec("UPDATE wake_attempts SET outcome=? WHERE project=? AND agent=? AND generation=? AND number=? AND outcome='dispatching'", outcome, p.Project, p.Agent, generation, Number(attempt));
        if (updated == 0) return false;
        _ = db.Exec("UPDATE wake_state SET state=CASE WHEN state='dispatching' THEN ? ELSE state END,last_outcome=?,updated=? WHERE project=? AND agent=? AND generation=? AND attempts=?", outcome == "transport-failed" ? "failed" : "sent", outcome, Time(), p.Project, p.Agent, generation, Number(attempt));
        Log(p.Project, "wake-" + outcome, null, p.Agent);
        return true;
    });

    public WakeConfirmation ConfirmWake(Principal p, string generation) => db.Transaction(() => {
        Registered(p);
        Required(generation, "generation");
        Sweep(p.Project);
        var (queued, leased) = PendingCounts(p);
        CompleteIfEmpty(p, queued, leased);
        var state = db.Query("SELECT state FROM wake_state WHERE project=? AND agent=? AND generation=?", p.Project, p.Agent, generation).FirstOrDefault()?[0];
        if (state is null or "ready" or "confirmed" or "completed" || queued + leased == 0 || db.Query("SELECT 1 FROM projects WHERE project=? AND paused=1", p.Project).Count != 0)
            return new WakeConfirmation(false, queued, leased);
        _ = db.Exec("UPDATE wake_state SET state='confirmed',confirmed_at=?,next_at=?,updated=? WHERE project=? AND agent=? AND generation=?", Time(), Number(checked(now() + wakeRetryMilliseconds)), Time(), p.Project, p.Agent, generation);
        Log(p.Project, "wake-confirmed", null, p.Agent);
        return new WakeConfirmation(true, queued, leased);
    });

    public bool FinishWake(Principal p, string generation) => db.Transaction(() => {
        Registered(p);
        Required(generation, "generation");
        Sweep(p.Project);
        var state = db.Query("SELECT state FROM wake_state WHERE project=? AND agent=? AND generation=?", p.Project, p.Agent, generation).FirstOrDefault()?[0];
        if (state == "completed") return true;
        if (state != "confirmed") return false;
        var (queued, leased) = PendingCounts(p);
        if (queued + leased == 0) CompleteIfEmpty(p, queued, leased);
        else if (queued > 0)
            _ = db.Exec("UPDATE wake_state SET generation=?,state='ready',next_at=?,confirmed_at=NULL,updated=? WHERE project=? AND agent=?", NewGeneration(), Time(), Time(), p.Project, p.Agent);
        // If only leased messages remain, keep the confirmed wake until a lease
        // expires; a later claim sweeps and re-arms it.
        Log(p.Project, "wake-finished", null, p.Agent);
        return true;
    });

    public WakeStatus GetWakeStatus(Principal p) => db.Transaction(() => {
        Registered(p);
        Sweep(p.Project);
        var (queued, leased) = PendingCounts(p);
        CompleteIfEmpty(p, queued, leased);
        if (queued + leased > 0) EnsureWake(p.Project, p.Agent);
        var row = db.Query("SELECT state,generation,attempts,next_at,last_outcome,confirmed_at FROM wake_state WHERE project=? AND agent=?", p.Project, p.Agent).FirstOrDefault();
        if (row is not null && row[0] == "dispatching" && long.Parse(row[3]!, CultureInfo.InvariantCulture) <= now() && MarkUnknownTransport(p, row[1]!)) {
            _ = db.Exec("UPDATE wake_state SET state='failed',updated=? WHERE project=? AND agent=? AND state='dispatching'", Time(), p.Project, p.Agent);
            row[0] = "failed";
            row[4] = "transport-unknown";
        }
        if (row is not null && queued > 0 && row[0] is not ("completed" or "exhausted") && int.Parse(row[2]!, CultureInfo.InvariantCulture) >= maxWakeAttempts && long.Parse(row[3]!, CultureInfo.InvariantCulture) <= now()) {
            if (MarkUnknownTransport(p, row[1]!)) row[4] = "transport-unknown";
            _ = db.Exec("UPDATE wake_state SET state='exhausted',updated=? WHERE project=? AND agent=?", Time(), p.Project, p.Agent);
            Log(p.Project, "wake-exhausted", null, p.Agent);
            row[0] = "exhausted";
        }
        var history = db.Query("SELECT generation,number,attempted_at,mode,outcome FROM wake_attempts WHERE project=? AND agent=? ORDER BY attempted_at DESC,rowid DESC LIMIT 20", p.Project, p.Agent)
            .Select(x => new WakeAttempt(x[0]!, int.Parse(x[1]!, CultureInfo.InvariantCulture), long.Parse(x[2]!, CultureInfo.InvariantCulture), x[3]!, x[4]!)).ToArray();
        return row is null ? new WakeStatus("idle", null, 0, null, null, null, queued, leased, history) :
            new WakeStatus(row[0]!, row[1], int.Parse(row[2]!, CultureInfo.InvariantCulture), row[0] is "completed" or "exhausted" ? null : long.Parse(row[3]!, CultureInfo.InvariantCulture), row[4], row[5] is null ? null : long.Parse(row[5]!, CultureInfo.InvariantCulture), queued, leased, history);
    });

    public RecipientWakeStatus[] PendingWakeStatuses(string project) {
        Required(project, "project");
        // Include deliveries created by older versions that have no wake row.
        var agents = db.Transaction(() => {
            Sweep(project);
            return db.Query("SELECT DISTINCT recipient FROM messages WHERE project=? AND status IN ('queued','leased') ORDER BY recipient LIMIT 100", project)
                .Select(x => x[0]!).ToArray();
        });
        return agents.Select(agent => {
            var identity = new Principal(project, agent);
            var binding = FindIdentity(identity);
            return new RecipientWakeStatus(agent, GetWakeStatus(identity), binding?.Runtime, binding?.SessionId);
        })
            .Where(x => x.Wake.Queued + x.Wake.Leased > 0).ToArray();
    }

    public bool RetryWake(Principal p) => db.Transaction(() => {
        Registered(p);
        Sweep(p.Project);
        var (queued, leased) = PendingCounts(p);
        if (queued + leased == 0) { CompleteIfEmpty(p, queued, leased); return false; }
        EnsureWake(p.Project, p.Agent);
        _ = db.Exec("UPDATE wake_state SET generation=?,state='ready',attempts=0,next_at=?,last_outcome=NULL,confirmed_at=NULL,updated=? WHERE project=? AND agent=?", NewGeneration(), Time(), Time(), p.Project, p.Agent);
        Log(p.Project, "wake-retry-requested", null, p.Agent);
        return true;
    });
}
