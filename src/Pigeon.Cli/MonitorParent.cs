using System.Globalization;
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
    private static readonly string[] Shells = ["bash", "sh", "dash", "zsh", "env", "cmd", "powershell", "pwsh"];

    internal readonly record struct Ancestor(string Image, long CreatedAt, bool Alive);
    internal enum Link { Stop, Last, Continue }

    // Decides whether to watch the next ancestor up and whether to keep walking. A process
    // that cannot be opened, has exited, or started after its child (its PID was reused
    // because the real parent is gone) ends the chain without being watched.
    internal static Link Classify(Ancestor? candidate, long childCreatedAt, int depth) {
        if (candidate is not { } ancestor || !ancestor.Alive || ancestor.CreatedAt > childCreatedAt) return Link.Stop;
        if (depth + 1 >= MaxDepth) return Link.Last;
        return Shells.Contains(ProgramName(ancestor.Image), StringComparer.OrdinalIgnoreCase) ? Link.Continue : Link.Last;
    }

    // The file name without directory or ".exe", for Windows and Unix paths alike.
    internal static string ProgramName(string image) {
        var name = image[(image.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private readonly Tracked[] watched;
    private MonitorParent(Tracked[] watched) => this.watched = watched;

    public static MonitorParent Capture() {
        using var self = Open(Environment.ProcessId) ?? throw new InvalidOperationException("Cannot inspect the monitor process.");
        var held = new List<Tracked>();
        try {
            var child = self;
            var childCreatedAt = self.CreatedAt;
            for (var depth = 0; depth < MaxDepth; depth++) {
                var parent = child.ParentId is int id ? Open(id) : null;
                var ancestor = parent is null ? (Ancestor?)null : new Ancestor(parent.Image, parent.CreatedAt, parent.IsRunning);
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
            foreach (var process in held) process.Dispose();
            throw;
        }
    }

    public bool IsAlive => watched.All(process => process.IsRunning);

    public void Dispose() {
        foreach (var process in watched) process.Dispose();
    }

    private static Tracked? Open(int processId) =>
        OperatingSystem.IsWindows() ? WindowsProcess.Open(processId)
        : OperatingSystem.IsLinux() ? LinuxProcess.Open(processId)
        : OperatingSystem.IsMacOS() ? MacProcess.Open(processId)
        : throw new PlatformNotSupportedException();

    // A process seen at capture time. Creation times are compared only within one platform.
    private abstract class Tracked : IDisposable {
        public abstract int? ParentId { get; }
        public abstract long CreatedAt { get; }
        public abstract string Image { get; }
        public abstract bool IsRunning { get; }
        public virtual void Dispose() { }
    }

    // Holds a handle, so a reused process ID cannot be mistaken for the original process.
    private sealed class WindowsProcess(SafeProcessHandle handle) : Tracked {
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

        public static WindowsProcess? Open(int processId) {
            var handle = OpenProcess(QueryLimitedInformation | Synchronize, false, processId);
            if (!handle.IsInvalid) return new WindowsProcess(handle);
            handle.Dispose();
            return null;
        }
        public override int? ParentId {
            get {
                var status = NtQueryInformationProcess(handle, 0, out var info, Marshal.SizeOf<BasicInformation>(), out _);
                var id = info.ParentProcessId.ToInt64();
                return status == 0 && id is > 0 and <= int.MaxValue ? (int)id : null;
            }
        }
        public override long CreatedAt => GetProcessTimes(handle, out var creation, out _, out _, out _) ? creation : long.MaxValue;
        public override string Image {
            get {
                var name = new char[1024];
                var size = name.Length;
                return QueryFullProcessImageNameW(handle, 0, name, ref size) ? new string(name, 0, size) : "";
            }
        }
        public override bool IsRunning {
            get {
                try { return !handle.IsClosed && WaitForSingleObject(handle, 0) == WaitTimeout; }
                catch (ObjectDisposedException) { return false; }
            }
        }
        public override void Dispose() => handle.Dispose();
    }

    // Unix has no portable process handle, so a process is identified by its ID and start
    // time: it is running while that ID still exists with the same start time and has not
    // become a zombie.
    private sealed class LinuxProcess(int processId, long startTicks, int? parentId, string image) : Tracked {
        public static LinuxProcess? Open(int processId) =>
            Stat(processId) is { } stat && stat.State != 'Z' ? new LinuxProcess(processId, stat.StartTicks, stat.ParentId, ReadImage(processId, stat.Command)) : null;
        public override int? ParentId => parentId;
        public override long CreatedAt => startTicks;
        public override string Image => image;
        public override bool IsRunning => Stat(processId) is { } stat && stat.State != 'Z' && stat.StartTicks == startTicks;

        private readonly record struct ProcStat(char State, int? ParentId, long StartTicks, string Command);

        // /proc/<pid>/stat is "pid (comm) state ppid ... starttime ...". The command may contain
        // spaces or parentheses, so the fields are counted from the last ')'.
        private static ProcStat? Stat(int processId) {
            string text;
            try { text = File.ReadAllText($"/proc/{processId}/stat"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            if (open < 0 || close < open) return null;
            var fields = text[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 || fields[0].Length != 1 || !long.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var start)) return null;
            int? parent = int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
            return new ProcStat(fields[0][0], parent, start, text[(open + 1)..close]);
        }
        private static string ReadImage(int processId, string command) {
            try { return new FileInfo($"/proc/{processId}/exe").LinkTarget ?? command; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return command; }
        }
    }

    private sealed class MacProcess(int processId, long startMicroseconds, int? parentId, string image) : Tracked {
        // proc_pidinfo(PROC_PIDTBSDINFO) fills a 136-byte proc_bsdinfo: pbi_status at offset 4,
        // pbi_ppid at 16, pbi_comm (16 bytes) at 48, and the start time (seconds, microseconds) at 120.
        private const int BsdInfoFlavor = 3, BsdInfoSize = 136, Zombie = 5;
        [DllImport("/usr/lib/libSystem.dylib")]
        private static extern int proc_pidinfo(int pid, int flavor, ulong argument, byte[] buffer, int size);
        [DllImport("/usr/lib/libSystem.dylib")]
        private static extern int proc_pidpath(int pid, byte[] buffer, uint size);

        public static MacProcess? Open(int processId) =>
            Info(processId) is { } info && info.Status != Zombie ? new MacProcess(processId, info.Start, info.ParentId, ReadImage(processId, info.Command)) : null;
        public override int? ParentId => parentId;
        public override long CreatedAt => startMicroseconds;
        public override string Image => image;
        public override bool IsRunning => Info(processId) is { } info && info.Status != Zombie && info.Start == startMicroseconds;

        private readonly record struct BsdInfo(uint Status, int? ParentId, long Start, string Command);

        private static BsdInfo? Info(int processId) {
            var buffer = new byte[BsdInfoSize];
            if (proc_pidinfo(processId, BsdInfoFlavor, 0, buffer, buffer.Length) != BsdInfoSize) return null;
            var parent = BitConverter.ToUInt32(buffer, 16);
            var start = checked((long)BitConverter.ToUInt64(buffer, 120) * 1_000_000 + (long)BitConverter.ToUInt64(buffer, 128));
            var end = Array.IndexOf(buffer, (byte)0, 48, 16);
            var command = System.Text.Encoding.UTF8.GetString(buffer, 48, (end < 0 ? 64 : end) - 48);
            return new BsdInfo(BitConverter.ToUInt32(buffer, 4), parent is > 0 and <= int.MaxValue ? (int)parent : null, start, command);
        }
        private static string ReadImage(int processId, string command) {
            var buffer = new byte[4096];
            var length = proc_pidpath(processId, buffer, (uint)buffer.Length);
            return length > 0 ? System.Text.Encoding.UTF8.GetString(buffer, 0, length) : command;
        }
    }
}
