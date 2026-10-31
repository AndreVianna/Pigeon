using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: InternalsVisibleTo("Pigeon.UnitTests")]

// A session monitor or hook waiter must not keep claiming wakes after its launcher exits.
// Hosts start it through one or more shells (Git Bash adds an intermediate bash), and a
// host that kills only its own child can leave those shells running. So the waiter holds
// every shell between itself and the first non-shell ancestor, plus that ancestor, and
// stops when any of them exits. It inspects only its own ancestors and never signals them.
internal sealed class MonitorParent : IDisposable {
    internal const int MaxDepth = 8;
    private static readonly string[] Shells = ["bash.exe", "sh.exe", "dash.exe", "env.exe", "cmd.exe", "powershell.exe", "pwsh.exe"];

    internal readonly record struct Ancestor(string Image, long CreatedAt, bool Alive);
    internal enum Link { Stop, Last, Continue }

    // Decides whether to watch the next ancestor up and whether to keep walking. A process
    // that cannot be opened, has exited, or started after its child (its PID was reused
    // because the real parent is gone) ends the chain without being watched.
    internal static Link Classify(Ancestor? candidate, long childCreatedAt, int depth) {
        if (candidate is not { } ancestor || !ancestor.Alive || ancestor.CreatedAt > childCreatedAt) return Link.Stop;
        if (depth + 1 >= MaxDepth) return Link.Last;
        return Shells.Contains(Path.GetFileName(ancestor.Image), StringComparer.OrdinalIgnoreCase) ? Link.Continue : Link.Last;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation {
        public IntPtr Reserved1, Peb, Reserved2A, Reserved2B, ProcessId, ParentProcessId;
    }

    private const uint QueryLimitedInformation = 0x1000, Synchronize = 0x00100000, WaitTimeout = 0x102;
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, out BasicInformation information, int length, out int returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, int flags, [Out] char[] name, ref int size);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    private readonly SafeProcessHandle[] watched;
    private MonitorParent(SafeProcessHandle[] watched) => this.watched = watched;

    public static MonitorParent Capture() {
        using var self = Open(Environment.ProcessId) ?? throw new InvalidOperationException("Cannot inspect the monitor process.");
        var held = new List<SafeProcessHandle>();
        try {
            var child = self;
            var childCreatedAt = CreatedAt(self);
            for (var depth = 0; depth < MaxDepth; depth++) {
                var parent = ParentId(child) is int id ? Open(id) : null;
                var ancestor = parent is null ? (Ancestor?)null : new Ancestor(Image(parent), CreatedAt(parent), IsRunning(parent));
                var link = Classify(ancestor, childCreatedAt, depth);
                if (link == Link.Stop) { parent?.Dispose(); break; }
                held.Add(parent!);
                if (link == Link.Last) break;
                (child, childCreatedAt) = (parent!, ancestor!.Value.CreatedAt);
            }
            if (held.Count == 0) throw new InvalidOperationException("The monitor's parent already exited or cannot be tracked.");
            return new MonitorParent([.. held]);
        }
        catch {
            foreach (var handle in held) handle.Dispose();
            throw;
        }
    }

    public bool IsAlive => watched.All(IsRunning);

    public void Dispose() {
        foreach (var handle in watched) handle.Dispose();
    }

    private static SafeProcessHandle? Open(int processId) {
        var handle = OpenProcess(QueryLimitedInformation | Synchronize, false, processId);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        return null;
    }
    private static int? ParentId(SafeProcessHandle process) {
        var status = NtQueryInformationProcess(process, 0, out var info, Marshal.SizeOf<BasicInformation>(), out _);
        var id = info.ParentProcessId.ToInt64();
        return status == 0 && id is > 0 and <= int.MaxValue ? (int)id : null;
    }
    private static long CreatedAt(SafeProcessHandle process) => GetProcessTimes(process, out var creation, out _, out _, out _) ? creation : long.MaxValue;
    private static string Image(SafeProcessHandle process) {
        var name = new char[1024];
        var size = name.Length;
        return QueryFullProcessImageNameW(process, 0, name, ref size) ? new string(name, 0, size) : "";
    }
    private static bool IsRunning(SafeProcessHandle process) {
        try { return !process.IsClosed && WaitForSingleObject(process, 0) == WaitTimeout; }
        catch (ObjectDisposedException) { return false; }
    }
}
