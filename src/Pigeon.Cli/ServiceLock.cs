using System.Security.Cryptography;
using System.Text;

// Allows one service per port for the current user. Windows uses a session-local named
// semaphore; Unix has none, so it holds an exclusive lock on a per-user file instead.
internal sealed class ServiceLock : IDisposable {
    private readonly Semaphore? semaphore;
    private readonly FileStream? file;
    private ServiceLock(Semaphore? semaphore, FileStream? file) => (this.semaphore, this.file) = (semaphore, file);

    public static ServiceLock? TryAcquire(string port) {
        var name = "Pigeon-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(port)));
        if (OperatingSystem.IsWindows()) {
            var held = new Semaphore(1, 1, "Local\\" + name);
            if (held.WaitOne(0))
                return new ServiceLock(held, null);
            held.Dispose();
            return null;
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) ? Path.GetTempPath() : root, "Pigeon");
        _ = Directory.CreateDirectory(directory);
        // FileShare.None takes an exclusive flock on Unix; a second service fails to open it.
        try {
            return new ServiceLock(null, new FileStream(Path.Combine(directory, name + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException) {
            return null;
        }
    }

    public void Dispose() {
        if (semaphore is not null) {
            _ = semaphore.Release();
            semaphore.Dispose();
        }

        file?.Dispose();
    }
}
