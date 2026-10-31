using System.Diagnostics;
using System.Runtime.InteropServices;

// A session monitor must not continue claiming wakes after its launcher exits.
internal sealed class MonitorParent : IDisposable {
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation {
        public IntPtr Reserved1, Peb, Reserved2A, Reserved2B, ProcessId, ParentProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, out BasicInformation information, int length, out int returnLength);

    private readonly Process process;
    private MonitorParent(Process process) => this.process = process;

    public static MonitorParent Capture() {
        using var current = Process.GetCurrentProcess();
        var status = NtQueryInformationProcess(current.Handle, 0, out var info, Marshal.SizeOf<BasicInformation>(), out _);
        if (status != 0 || info.ParentProcessId.ToInt64() <= 0 || info.ParentProcessId.ToInt64() > int.MaxValue)
            throw new InvalidOperationException("Cannot identify the monitor's parent process.");
        try {
            var parent = Process.GetProcessById((int)info.ParentProcessId.ToInt64());
            if (parent.HasExited) { parent.Dispose(); throw new InvalidOperationException("The monitor's parent already exited."); }
            return new MonitorParent(parent);
        }
        catch (ArgumentException e) { throw new InvalidOperationException("The monitor's parent already exited.", e); }
    }

    public bool IsAlive {
        get {
            try { return !process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public void Dispose() => process.Dispose();
}
