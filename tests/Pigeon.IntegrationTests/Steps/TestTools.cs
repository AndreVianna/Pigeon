namespace Pigeon.IntegrationTests.Steps;

// Programs the tests start, located for the current operating system.
internal static class TestTools {
    // The CLI built next to the tests.
    public static string Pigeon => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "pigeon.exe" : "pigeon");

    // Git Bash on Windows, the system bash elsewhere.
    public static string Bash => Environment.GetEnvironmentVariable("PIGEON_TEST_BASH")
        ?? (OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") : "/bin/bash");

    // The `sh` the Claude plugin's hooks run under: Git's sh on Windows, the system sh (dash on Ubuntu) elsewhere.
    public static string Sh => OperatingSystem.IsWindows()
        ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Bash)!, "..", "usr", "bin", "sh.exe"))
        : "/bin/sh";
}
