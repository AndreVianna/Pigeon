using System.Runtime.InteropServices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Pigeon.IntegrationTests")]

namespace Pigeon.Core;

// A storage condition that may clear on its own (a held lock, I/O or space trouble).
// Callers should retry it rather than treat it as a rejected request.
public sealed class StorageUnavailableException(string message) : Exception(message);

// Narrow Windows SQLite binding: parameterized statements only, no external packages.
internal sealed class Sqlite : IDisposable {
    private IntPtr db;
    private const int Row = 100, Done = 101;
    private static readonly IntPtr Transient = new(-1);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr stmt, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, IntPtr destructor);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_null(IntPtr stmt, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_changes(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_get_autocommit(IntPtr db);

    public Sqlite(string path) {
        var rc = sqlite3_open_v2(path, out db, 2 | 4 | 0x10000, IntPtr.Zero);
        if (rc != 0) {
            var message = Error();
            Dispose();
            throw new InvalidOperationException(message);
        }

        // Shorter than the 3-second client timeout, so a held lock is reported as storage unavailable.
        Check(sqlite3_busy_timeout(db, 2000));
        _ = Exec("PRAGMA foreign_keys=ON");
        _ = Exec("PRAGMA journal_mode=WAL");
    }
    private string Error() => db == IntPtr.Zero ? "SQLite open failed" : Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "SQLite error";
    private void Check(int rc) {
        if (rc != 0)
            throw Failure(rc);
    }
    // BUSY, LOCKED, NOMEM, IOERR, FULL, CANTOPEN and PROTOCOL are transient; constraint
    // and other errors remain rejections of the request.
    private Exception Failure(int rc) => (rc & 0xFF) is 5 or 6 or 7 or 10 or 13 or 14 or 15
        ? new StorageUnavailableException(Error())
        : new InvalidOperationException(Error());
    public List<string?[]> Query(string sql, params string?[] args) {
        Check(sqlite3_prepare_v2(db, sql, -1, out var stmt, IntPtr.Zero));
        try {
            for (var i = 0; i < args.Length; i++)
                Check(args[i] is null ? sqlite3_bind_null(stmt, i + 1) : sqlite3_bind_text(stmt, i + 1, args[i]!, -1, Transient));
            var rows = new List<string?[]>();
            int rc;
            while ((rc = sqlite3_step(stmt)) == Row) {
                var row = new string?[sqlite3_column_count(stmt)];
                for (var i = 0; i < row.Length; i++)
                    row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i));
                rows.Add(row);
            }

            return rc != Done ? throw Failure(rc) : rows;
        }
        finally {
            _ = sqlite3_finalize(stmt);
        }
    }
    public int Exec(string sql, params string?[] args) {
        _ = Query(sql, args);
        return sqlite3_changes(db);
    }
    public T Transaction<T>(Func<T> action) {
        _ = Exec("BEGIN IMMEDIATE");
        try {
            var value = action();
            _ = Exec("COMMIT");
            return value;
        }
        catch {
            // SQLite may already have rolled back (for example after an I/O error);
            // a second ROLLBACK would fail and hide the original error.
            if (sqlite3_get_autocommit(db) == 0)
                _ = Exec("ROLLBACK");
            throw;
        }
    }
    public void Dispose() {
        if (db != IntPtr.Zero) {
            _ = sqlite3_close_v2(db);
            db = IntPtr.Zero;
        }
    }
}
