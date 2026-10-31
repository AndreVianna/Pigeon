using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

internal static class MonitorOutput {
    private const int StdOutputHandle = -11;
    private const uint FileTypePipe = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(IntPtr handle, byte[] buffer, uint length, out uint written, IntPtr overlapped);

    public static async Task WriteLineAsync(string value, CancellationToken cancellation) {
        var handle = GetStdHandle(StdOutputHandle);
        if (GetFileType(handle) != FileTypePipe) {
            await Console.Out.WriteLineAsync(value);
            await Console.Out.FlushAsync(cancellation);
            return;
        }
        // Console's Windows writer can suppress a broken pipe. Check WriteFile's
        // result directly so a vanished monitor listener cannot silently consume wakes.
        var bytes = Encoding.UTF8.GetBytes(value + "\n");
        if (!WriteFile(handle, bytes, (uint)bytes.Length, out var written, IntPtr.Zero) || written != bytes.Length)
            throw new IOException("The monitor output pipe is unavailable.", new Win32Exception(Marshal.GetLastWin32Error()));
    }
}
