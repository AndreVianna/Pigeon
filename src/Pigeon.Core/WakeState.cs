using System.Globalization;

namespace Pigeon.Core;

public sealed record WakeClaim(string Generation, int Attempt);
// Accepted is false for a stale or already finished wake; Messages are leased to it until it completes.
public sealed record WakeBatch(bool Accepted, Delivery[] Messages, int Remaining);
public sealed record WakeAttempt(string Generation, int Number, long AttemptedAtUnixMs, string Mode, string Outcome);
public sealed record WakeStatus(string State, string? Generation, int Attempts, long? NextAttemptUnixMs, string? LastOutcome, long? ConfirmedAtUnixMs, int Queued, int Leased, WakeAttempt[] History);
public sealed record SessionWakeStatus(string Tool, string SessionId, WakeStatus Wake);

public sealed partial class Mailbox {
    private static string NewGeneration() => Guid.NewGuid().ToString("N");

    // Called inside the transaction that queues a message. Unprocessed work shares one
    // generation even during a burst.
    private void EnsureWake(string session) {
        var existing = db.Query("SELECT state FROM p_wake_state WHERE session=?", session).FirstOrDefault();
        if (existing is null)
            _ = db.Exec("INSERT INTO p_wake_state(session,generation,state,attempts,next_at,updated) VALUES(?,?,'ready',0,?,?)", session, NewGeneration(), Time(), Time());
        else if (existing[0] == "completed")
            _ = db.Exec("UPDATE p_wake_state SET generation=?,state='ready',attempts=0,next_at=?,last_outcome=NULL,confirmed_at=NULL,updated=? WHERE session=?", NewGeneration(), Time(), Time(), session);
    }

    // A new message gives an exhausted wake one fresh, bounded budget, so a single stuck
    // message cannot silence every later one.
    private void WakeForNewMessage(string session) {
        _ = db.Exec("UPDATE p_wake_state SET generation=?,state='ready',attempts=0,next_at=?,last_outcome=NULL,confirmed_at=NULL,updated=? WHERE session=? AND state='exhausted'", NewGeneration(), Time(), Time(), session);
        EnsureWake(session);
    }

    private (int Queued, int Leased) PendingCounts(string session) {
        var rows = db.Query("SELECT status,COUNT(*) FROM p_messages WHERE recipient=? AND status IN ('queued','leased') GROUP BY status", session)
            .ToDictionary(row => row[0]!, row => int.Parse(row[1]!, CultureInfo.InvariantCulture));
        return (rows.GetValueOrDefault("queued"), rows.GetValueOrDefault("leased"));
    }

    // After messages were removed or read: finish an empty wake, or reset the retry budget
    // of a confirmed one that made progress.
    private void SettleWake(string session, bool progress = false) {
        var (queued, leased) = PendingCounts(session);
        if (queued == 0 && leased == 0)
            _ = db.Exec("UPDATE p_wake_state SET state='completed',next_at=0,updated=? WHERE session=? AND state<>'completed'", Time(), session);
        else if (progress)
            _ = db.Exec("UPDATE p_wake_state SET attempts=0,updated=? WHERE session=? AND state='confirmed'", Time(), session);
    }

    private bool MarkUnknownTransport(string session, string generation) {
        var changed = db.Exec("UPDATE p_wake_attempts SET outcome='transport-unknown' WHERE session=? AND generation=? AND outcome='dispatching'", session, generation);
        if (changed > 0)
            _ = db.Exec("UPDATE p_wake_state SET last_outcome='transport-unknown',updated=? WHERE session=?", Time(), session);
        return changed > 0;
    }

    public WakeClaim? ClaimWake(string session, string mode) {
        Required(session, "session ID");
        if (mode is not ("codex-queue" or "claude-hook")) throw new ArgumentException("Invalid wake mode");
        return db.Transaction(() => {
            var tool = ToolOf(session) ?? throw new InvalidOperationException($"Session {session} is not known.");
            if ((mode == "codex-queue" && tool != "codex") || (mode == "claude-hook" && tool != "claude-code"))
                throw new InvalidOperationException("Wake mode does not match the session's tool.");
            Sweep();
            var (queued, leased) = PendingCounts(session);
            if (queued == 0) {
                if (leased == 0) SettleWake(session);
                return null;
            }
            EnsureWake(session);
            var row = db.Query("SELECT generation,state,attempts,next_at FROM p_wake_state WHERE session=?", session)[0];
            var generation = row[0]!;
            var state = row[1]!;
            var attempts = int.Parse(row[2]!, CultureInfo.InvariantCulture);
            if (state == "exhausted" || Long(row[3]) > now()) return null;
            _ = MarkUnknownTransport(session, generation);
            if (attempts >= maxWakeAttempts) {
                _ = db.Exec("UPDATE p_wake_state SET state='exhausted',updated=? WHERE session=?", Time(), session);
                return null;
            }
            // A confirmed but unfinished wake gets a new generation; a delayed old prompt
            // must never drain a later batch.
            if (state == "confirmed") generation = NewGeneration();
            attempts++;
            _ = db.Exec("UPDATE p_wake_state SET generation=?,state='dispatching',attempts=?,next_at=?,confirmed_at=NULL,updated=? WHERE session=?", generation, Number(attempts), Number(checked(now() + wakeRetryMilliseconds)), Time(), session);
            _ = db.Exec("INSERT INTO p_wake_attempts(session,generation,number,attempted_at,mode,outcome) VALUES(?,?,?,?,?,'dispatching')", session, generation, Number(attempts), Time(), mode);
            return new WakeClaim(generation, attempts);
        });
    }

    public bool RecordWakeResult(string session, string generation, int attempt, string outcome) {
        Required(session, "session ID");
        Required(generation, "generation");
        if (attempt < 1 || outcome is not ("transport-accepted" or "transport-written" or "transport-failed")) throw new ArgumentException("Invalid wake result");
        return db.Transaction(() => {
            if (db.Exec("UPDATE p_wake_attempts SET outcome=? WHERE session=? AND generation=? AND number=? AND outcome='dispatching'", outcome, session, generation, Number(attempt)) == 0) return false;
            _ = db.Exec("UPDATE p_wake_state SET state=CASE WHEN state='dispatching' THEN ? ELSE state END,last_outcome=?,updated=? WHERE session=? AND generation=? AND attempts=?", outcome == "transport-failed" ? "failed" : "sent", outcome, Time(), session, generation, Number(attempt));
            return true;
        });
    }

    // Confirms the wake on first use and leases the next batch to it. Calling again with the
    // same generation returns the following batch.
    public WakeBatch ReceiveForWake(string session, string generation, int max = 20) {
        Required(session, "session ID");
        Required(generation, "generation");
        if (max is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(max));
        return db.Transaction(() => {
            Sweep();
            var (queued, leased) = PendingCounts(session);
            var state = db.Query("SELECT state FROM p_wake_state WHERE session=? AND generation=?", session, generation).FirstOrDefault()?[0];
            if (state is null or "ready" or "completed" || (state != "confirmed" && queued == 0)) {
                if (queued + leased == 0) SettleWake(session);
                return new WakeBatch(false, [], queued);
            }
            if (state != "confirmed")
                _ = db.Exec("UPDATE p_wake_state SET state='confirmed',confirmed_at=?,next_at=?,updated=? WHERE session=?", Time(), Number(checked(now() + wakeRetryMilliseconds)), Time(), session);
            var rows = db.Query("SELECT id,topic,sender,body,created FROM p_messages WHERE recipient=? AND status='queued' ORDER BY created,rowid LIMIT ?", session, Number(max));
            foreach (var row in rows)
                _ = db.Exec("UPDATE p_messages SET status='leased',attempts=attempts+1,lease_until=?,lease_generation=? WHERE id=?", Number(checked(now() + leaseMilliseconds)), generation, row[0]);
            return new WakeBatch(true, rows.Select(ToDelivery).ToArray(), queued - rows.Count);
        });
    }

    // Completing a wake acknowledges every message it received.
    public bool FinishWake(string session, string generation) {
        Required(session, "session ID");
        Required(generation, "generation");
        return db.Transaction(() => {
            Sweep();
            var state = db.Query("SELECT state FROM p_wake_state WHERE session=? AND generation=?", session, generation).FirstOrDefault()?[0];
            if (state == "completed") return true;
            if (state != "confirmed") return false;
            _ = db.Exec("UPDATE p_messages SET status='acknowledged',terminal_at=?,lease_until=NULL WHERE recipient=? AND lease_generation=? AND status IN ('leased','queued')", Time(), session, generation);
            var (queued, leased) = PendingCounts(session);
            if (queued + leased == 0) SettleWake(session);
            else if (queued > 0)
                _ = db.Exec("UPDATE p_wake_state SET generation=?,state='ready',attempts=0,next_at=?,confirmed_at=NULL,updated=? WHERE session=?", NewGeneration(), Time(), Time(), session);
            // If only messages leased by another wake remain, a later claim re-arms once they time out.
            return true;
        });
    }

    public WakeStatus GetWakeStatus(string session) {
        Required(session, "session ID");
        return db.Transaction(() => StatusCore(session));
    }

    private WakeStatus StatusCore(string session) {
        Sweep();
        var (queued, leased) = PendingCounts(session);
        if (queued + leased == 0) SettleWake(session);
        else EnsureWake(session);
        var row = db.Query("SELECT state,generation,attempts,next_at,last_outcome,confirmed_at FROM p_wake_state WHERE session=?", session).FirstOrDefault();
        if (row is not null && row[0] == "dispatching" && Long(row[3]) <= now() && MarkUnknownTransport(session, row[1]!)) {
            _ = db.Exec("UPDATE p_wake_state SET state='failed',updated=? WHERE session=? AND state='dispatching'", Time(), session);
            row[0] = "failed";
            row[4] = "transport-unknown";
        }
        if (row is not null && queued > 0 && row[0] is not ("completed" or "exhausted") && int.Parse(row[2]!, CultureInfo.InvariantCulture) >= maxWakeAttempts && Long(row[3]) <= now()) {
            if (MarkUnknownTransport(session, row[1]!)) row[4] = "transport-unknown";
            _ = db.Exec("UPDATE p_wake_state SET state='exhausted',updated=? WHERE session=?", Time(), session);
            row[0] = "exhausted";
        }
        var history = db.Query("SELECT generation,number,attempted_at,mode,outcome FROM p_wake_attempts WHERE session=? ORDER BY attempted_at DESC,rowid DESC LIMIT 20", session)
            .Select(x => new WakeAttempt(x[0]!, int.Parse(x[1]!, CultureInfo.InvariantCulture), Long(x[2]), x[3]!, x[4]!)).ToArray();
        return row is null ? new WakeStatus("idle", null, 0, null, null, null, queued, leased, history) :
            new WakeStatus(row[0]!, row[1], int.Parse(row[2]!, CultureInfo.InvariantCulture), row[0] is "completed" or "exhausted" ? null : Long(row[3]), row[4], row[5] is null ? null : Long(row[5]), queued, leased, history);
    }

    // Sessions with messages waiting, for status reports.
    public SessionWakeStatus[] PendingWakeStatuses() => db.Transaction(() => {
        Sweep();
        return db.Query("SELECT DISTINCT m.recipient,s.tool FROM p_messages m JOIN p_sessions s ON s.session=m.recipient WHERE m.status IN ('queued','leased') ORDER BY m.recipient LIMIT 100")
            .Select(r => new SessionWakeStatus(r[1]!, r[0]!, StatusCore(r[0]!))).ToArray();
    });
}
