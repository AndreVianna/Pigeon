using Pigeon.Service;

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// `pigeon update`: installs the newest release from GitHub the way this copy of Pigeon was
// installed (MSI, pkg, deb, rpm or the Linux tarball). Without --allow-pre-release it takes only
// stable releases, and it never installs an older version than the one running.
internal static class Updater {
    internal const string Repository = "AndreVianna/Pigeon";
    private const string ReleasesUrl = $"https://api.github.com/repos/{Repository}/releases?per_page=100";
    internal const string DownloadPrefix = $"https://github.com/{Repository}/releases/download/";
    private const long MaxDownload = 200L * 1024 * 1024;

    public static async Task<int> RunAsync(string port, bool allowPreRelease, HttpMessageHandler? handler = null) {
        // The caller reports an IOException as "the service is not running"; here it is a file
        // that could not be written or moved.
        try {
            return await UpdateAsync(port, allowPreRelease, handler);
        }
        catch (IOException e) {
            throw new CommandException($"The update failed: {e.GetType().Name} {e.Message} (0x{e.HResult:X8})".TrimEnd());
        }
    }

    private static async Task<int> UpdateAsync(string port, bool allowPreRelease, HttpMessageHandler? handler) {
        var current = SemVersion.Parse(CurrentVersion()) ?? throw new CommandException("This Pigeon has no release version, so it cannot update itself.");
        var install = InstallInfo.Detect();
        if (install.Kind == InstallKind.Unsupported)
            throw new CommandException(install.Reason);
        install.CleanUpOldCopies();

        using var http = Client(handler);
        var releases = await FetchReleasesAsync(http, Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
        var choice = ReleaseChoice.Choose(releases, current, allowPreRelease);
        if (choice.Newest is null) {
            Console.WriteLine(UpToDate(current, choice, allowPreRelease));
            return 0;
        }

        var release = choice.Newest;
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(install.AssetSuffix, StringComparison.Ordinal))
            ?? throw new CommandException($"Pigeon {release.Version} has no installer for this system (looked for a file ending in {install.AssetSuffix}). Download it from https://github.com/{Repository}/releases.");
        var sums = release.Assets.FirstOrDefault(a => a.Name == "SHA256SUMS.txt")
            ?? throw new CommandException($"Pigeon {release.Version} has no SHA256SUMS.txt, so its download cannot be checked. Install it from https://github.com/{Repository}/releases.");
        Console.WriteLine($"Updating Pigeon {current} to {release.Version}{(release.PreRelease ? " (pre-release)" : "")}.");

        var folder = Directory.CreateTempSubdirectory("pigeon-update-");
        try {
            var expected = Checksums.Find(await DownloadTextAsync(http, sums), asset.Name)
                ?? throw new CommandException($"SHA256SUMS.txt of Pigeon {release.Version} does not list {asset.Name}.");
            var file = Path.Combine(folder.FullName, asset.Name);
            Console.WriteLine($"Downloading {asset.Name} ({asset.Size / 1048576.0:0.0} MB)...");
            await DownloadAsync(http, asset, file, expected);

            var wasRunning = await ServiceRunningAsync(port);
            if (wasRunning)
                await StopServiceAsync(port);
            LoadAllAssemblies();
            try {
                await install.InstallAsync(file, release.Version);
            }
            finally {
                // Also after a failed install, which leaves the old version in place.
                if (wasRunning && await RunInstalledAsync(install.Executable, "start") != 0)
                    Console.Error.WriteLine("The Pigeon service did not start again. Run 'pigeon start'.");
            }
        }
        finally {
            try { folder.Delete(recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Console.WriteLine($"Pigeon {release.Version} is installed. Restart your agent apps so they use it.");
        Console.WriteLine("Their Pigeon plugins are separate: update them as the README describes.");
        return 0;
    }

    // A compressed single-file program loads an assembly from its own file the first time it is
    // needed. Once the installer moves or replaces that file, a load fails with
    // FileNotFoundException (seen in CI on Windows), so everything is loaded before the swap.
    internal static int LoadAllAssemblies() {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(Updater).Assembly]);
        while (pending.Count > 0)
            foreach (var name in pending.Dequeue().GetReferencedAssemblies())
                if (seen.Add(name.FullName)) {
                    try { pending.Enqueue(Assembly.Load(name)); }
                    catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException) { }
                }

        return seen.Count;
    }

    internal static string UpToDate(SemVersion current, ReleaseChoice choice, bool allowPreRelease)
        => allowPreRelease || choice.NewestStable is null ? $"Pigeon {current} is the newest version."
            : current.CompareTo(choice.NewestStable.Version) > 0 ? $"Pigeon {current} is newer than the newest stable version ({choice.NewestStable.Version}). Run 'pigeon update --allow-pre-release' to get newer pre-releases."
            : $"Pigeon {current} is the newest stable version.";

    internal static string CurrentVersion()
        => (typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];

    private static HttpClient Client(HttpMessageHandler? handler) {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromMinutes(5);
        // GitHub refuses API calls without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("pigeon", CurrentVersion() is { Length: > 0 } v ? v : "dev"));
        return http;
    }

    internal static async Task<IReadOnlyList<Release>> FetchReleasesAsync(HttpClient http, string? token = null) {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        // Optional, for machines that share an address with many others (CI): unauthenticated
        // calls are limited to 60 an hour. The token goes to api.github.com only.
        // The asset downloads never carry it, so it cannot follow their redirect to the storage host.
        var hasToken = !string.IsNullOrWhiteSpace(token);
        if (hasToken)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(http, request);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized)
            throw new CommandException(hasToken
                ? "GitHub refused the GITHUB_TOKEN this command sent with the request. Check that the token is valid and can read the repository's releases, or remove it to try without one."
                : "GitHub refused the request, most likely because this network made too many unauthenticated requests in the last hour. Try again later, or set GITHUB_TOKEN to a GitHub token to raise the limit.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new CommandException($"GitHub could not find the releases of {Repository}. If the repository is not public, set GITHUB_TOKEN to a token that can read it and run the command again.");
        if (!response.IsSuccessStatusCode)
            throw new CommandException($"GitHub answered {(int)response.StatusCode} when asked for Pigeon's releases.");
        await using var body = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(body);
        return Release.ParseAll(json.RootElement);
    }

    private static async Task<string> DownloadTextAsync(HttpClient http, ReleaseAsset asset) {
        using var request = new HttpRequestMessage(HttpMethod.Get, CheckedUrl(asset));
        using var response = await SendAsync(http, request);
        if (!response.IsSuccessStatusCode)
            throw new CommandException($"Downloading {asset.Name} failed: GitHub answered {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync();
    }

    internal static async Task DownloadAsync(HttpClient http, ReleaseAsset asset, string path, string expectedSha256) {
        using var request = new HttpRequestMessage(HttpMethod.Get, CheckedUrl(asset));
        using var response = await SendAsync(http, request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new CommandException($"Downloading {asset.Name} failed: GitHub answered {(int)response.StatusCode}.");
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(path)) {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0) {
                total += read;
                if (total > MaxDownload)
                    throw new CommandException($"{asset.Name} is larger than expected; the download was stopped.");
                await target.WriteAsync(buffer.AsMemory(0, read));
            }
        }

        string actual;
        await using (var check = File.OpenRead(path))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase)) {
            File.Delete(path);
            throw new CommandException($"{asset.Name} does not match its SHA256 in SHA256SUMS.txt, so it was not installed.");
        }
    }

    // Assets come only from this repository's releases, over HTTPS (GitHub then redirects to its
    // own download host).
    private static Uri CheckedUrl(ReleaseAsset asset)
        => asset.Url.StartsWith(DownloadPrefix, StringComparison.Ordinal) && Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri)
            ? uri
            : throw new CommandException($"{asset.Name} is not served from Pigeon's GitHub releases; it was not downloaded.");

    private static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead) {
        try {
            return await http.SendAsync(request, completion);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) {
            throw new CommandException($"Could not reach GitHub ({request.RequestUri?.Host}). Check the internet connection and try again.");
        }
    }

    private static async Task<bool> ServiceRunningAsync(string port) {
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var status = await LocalHttpClient.ExecuteAsync(port, "service_status", new { }, timeout.Token);
            return status.GetProperty("Ok").GetBoolean();
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or HttpRequestException) {
            return false;
        }
    }

    private static async Task StopServiceAsync(string port) {
        _ = await LocalHttpClient.ExecuteAsync(port, "service_stop", new { });
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10)) {
            if (!await ServiceRunningAsync(port)) {
                Console.WriteLine("Stopped the Pigeon service; it starts again after the update.");
                return;
            }

            await Task.Delay(100);
        }

        throw new CommandException("The Pigeon service did not stop, so nothing was installed. Run 'pigeon stop' and try again.");
    }

    internal static Task<int> RunInstalledAsync(string executable, params string[] arguments) => RunProgramAsync(executable, arguments);

    internal static async Task<int> RunProgramAsync(string program, params string[] arguments) {
        var info = new ProcessStartInfo(program) { UseShellExecute = false };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        try {
            using var process = Process.Start(info) ?? throw new CommandException($"Could not run {program}.");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) {
            return -1;
        }
    }

    // Runs a check quietly and reports whether it succeeded (false when the program is missing).
    internal static bool Succeeds(string program, params string[] arguments) {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        try {
            using var process = Process.Start(info);
            if (process is null)
                return false;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) {
            return false;
        }
    }
}

internal sealed record ReleaseAsset(string Name, string Url, long Size);

internal sealed record Release(string Tag, SemVersion Version, bool PreRelease, IReadOnlyList<ReleaseAsset> Assets) {
    // Drafts and tags that are not versions are skipped.
    public static IReadOnlyList<Release> ParseAll(JsonElement root) {
        var releases = new List<Release>();
        if (root.ValueKind != JsonValueKind.Array)
            return releases;
        foreach (var item in root.EnumerateArray()) {
            if (item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                continue;
            var tag = item.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            var version = SemVersion.Parse(tag.StartsWith('v') ? tag[1..] : tag);
            if (version is null)
                continue;
            var pre = item.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            var assets = new List<ReleaseAsset>();
            if (item.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var a in list.EnumerateArray())
                    if (a.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                        a.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String)
                        assets.Add(new ReleaseAsset(name.GetString()!, url.GetString()!, a.TryGetProperty("size", out var size) && size.TryGetInt64(out var s) ? s : 0));
            releases.Add(new Release(tag, version, pre, assets));
        }

        return releases;
    }
}

internal sealed record ReleaseChoice(Release? Newest, Release? NewestStable) {
    // Newest: the release to install, or null when none is newer than the running version.
    // NewestStable: for the message when only pre-releases are newer.
    public static ReleaseChoice Choose(IEnumerable<Release> releases, SemVersion current, bool allowPreRelease) {
        var all = releases.ToList();
        var stable = all.Where(r => !r.PreRelease && r.Version.IsStable).MaxBy(r => r.Version);
        var best = (allowPreRelease ? all : all.Where(r => !r.PreRelease && r.Version.IsStable)).MaxBy(r => r.Version);
        return new ReleaseChoice(best is not null && best.Version.CompareTo(current) > 0 ? best : null, stable);
    }
}

internal static class Checksums {
    // sha256sum output: "<hash>  <name>". GitHub replaces "~" in asset names with ".", so the deb
    // packages (pigeon_0.7.0~beta.1_amd64.deb) are listed under their original name.
    public static string? Find(string text, string assetName) {
        foreach (var line in text.Split('\n')) {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit))
                continue;
            var name = parts[1].Trim().TrimStart('*');
            if (name == assetName || name.Replace('~', '.') == assetName)
                return parts[0].ToLowerInvariant();
        }

        return null;
    }
}

// Semantic version precedence (semver.org §11): 0.7.0-beta.2 < 0.7.0-beta.10 < 0.7.0.
internal sealed record SemVersion(int Major, int Minor, int Patch, string[] Pre) : IComparable<SemVersion> {
    public bool IsStable => Pre.Length == 0;

    public static SemVersion? Parse(string text) {
        var core = text.Split('+')[0];
        var dash = core.IndexOf('-');
        var numbers = (dash < 0 ? core : core[..dash]).Split('.');
        if (numbers.Length != 3 || !numbers.All(n => n.Length > 0 && n.All(char.IsAsciiDigit)))
            return null;
        var pre = dash < 0 ? [] : core[(dash + 1)..].Split('.');
        if (pre.Any(p => p.Length == 0))
            return null;
        return int.TryParse(numbers[0], out var major) && int.TryParse(numbers[1], out var minor) && int.TryParse(numbers[2], out var patch)
            ? new SemVersion(major, minor, patch, pre)
            : null;
    }

    public int CompareTo(SemVersion? other) {
        if (other is null)
            return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
            return core;
        if (Pre.Length == 0 || other.Pre.Length == 0)
            return Pre.Length == other.Pre.Length ? 0 : Pre.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++) {
            var a = Pre[i];
            var b = other.Pre[i];
            var aNumber = a.All(char.IsAsciiDigit);
            var bNumber = b.All(char.IsAsciiDigit);
            var step = aNumber && bNumber ? a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b)
                : aNumber ? -1
                : bNumber ? 1
                : string.CompareOrdinal(a, b);
            if (step != 0)
                return Math.Sign(step);
        }

        return Pre.Length.CompareTo(other.Pre.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Pre.Length > 0 ? "-" + string.Join('.', Pre) : "")}";
    public bool Equals(SemVersion? other) => other is not null && CompareTo(other) == 0;
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', Pre));
}

internal enum InstallKind { Unsupported, Msi, Pkg, Deb, Rpm, Tarball }

// How this copy of Pigeon was installed, which decides the release file and how it is installed.
internal sealed class InstallInfo {
    public required InstallKind Kind { get; init; }
    public string Executable { get; init; } = "";
    public string AssetSuffix { get; init; } = "";
    public string Reason { get; init; } = "";

    private static InstallInfo Unsupported(string reason) => new() { Kind = InstallKind.Unsupported, Reason = reason };

    public static InstallInfo Detect() {
        var executable = Environment.ProcessPath ?? "";
        // A .NET tool or a development build runs from a dll; only the installers' single file can
        // be replaced by an installer.
#pragma warning disable IL3000
        var singleFile = string.IsNullOrEmpty(typeof(InstallInfo).Assembly.Location);
#pragma warning restore IL3000
        if (!singleFile)
            return Unsupported("This Pigeon runs as a .NET tool or from a build folder. Update a .NET tool with 'dotnet tool update --global Pigeon.Cli', or install Pigeon from https://github.com/" + Updater.Repository + "/releases.");
        var arch = RuntimeInformation.ProcessArchitecture;
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        return Detect(os, executable, arch, MsiFolder(),
            program => program switch {
                "pkg" => Updater.Succeeds("pkgutil", "--pkg-info", "io.github.andrevianna.pigeon"),
                "deb" => Updater.Succeeds("dpkg-query", "-S", executable),
                "rpm" => Updater.Succeeds("rpm", "-qf", executable),
                _ => false
            });
    }

    // owns(kind) asks the system's package manager whether it installed the executable.
    internal static InstallInfo Detect(string os, string executable, Architecture arch, string? msiFolder, Func<string, bool> owns) {
        if (os == "windows") {
            var folder = executable[..Math.Max(0, executable.LastIndexOf('\\'))];
            if (msiFolder is null || !string.Equals(msiFolder.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return Unsupported("This Pigeon was not installed with the Windows installer (.msi), so it cannot update itself. Install the .msi from https://github.com/" + Updater.Repository + "/releases.");
            return arch == Architecture.X64 || arch == Architecture.Arm64
                ? new InstallInfo { Kind = InstallKind.Msi, Executable = executable, AssetSuffix = "-win-x64.msi" }
                : Unsupported("Pigeon's Windows installer is for x64 and Arm64 PCs only.");
        }

        if (os == "macos") {
            var name = arch switch { Architecture.Arm64 => "osx-arm64", Architecture.X64 => "osx-x64", _ => null };
            if (name is null)
                return Unsupported("Pigeon has no installer for this Mac's processor.");
            return executable == "/usr/local/bin/pigeon" && owns("pkg")
                ? new InstallInfo { Kind = InstallKind.Pkg, Executable = executable, AssetSuffix = $"-{name}.pkg" }
                : Unsupported("This Pigeon was not installed with the macOS package (.pkg), so it cannot update itself. Install the .pkg from https://github.com/" + Updater.Repository + "/releases.");
        }

        var (deb, rpm, tar) = arch switch {
            Architecture.X64 => ("_amd64.deb", ".x86_64.rpm", "-linux-x64.tar.gz"),
            Architecture.Arm64 => ("_arm64.deb", ".aarch64.rpm", "-linux-arm64.tar.gz"),
            _ => (null, null, null)
        };
        if (deb is null)
            return Unsupported("Pigeon has no installer for this processor.");
        if (owns("deb"))
            return new InstallInfo { Kind = InstallKind.Deb, Executable = executable, AssetSuffix = deb };
        if (owns("rpm"))
            return new InstallInfo { Kind = InstallKind.Rpm, Executable = executable, AssetSuffix = rpm! };
        return new InstallInfo { Kind = InstallKind.Tarball, Executable = executable, AssetSuffix = tar! };
    }

    private static string? MsiFolder() {
        if (!OperatingSystem.IsWindows())
            return null;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Pigeon");
        return key?.GetValue("InstallFolder") as string;
    }

    // A replaced pigeon.exe keeps running in agent apps until they restart. Earlier updates moved
    // it to the temp folder (or, on another drive, next to the new one); it is deleted here once
    // nothing runs it.
    public void CleanUpOldCopies() {
        if (Kind != InstallKind.Msi)
            return;
        var folder = Path.GetDirectoryName(Executable);
        var old = Directory.EnumerateFiles(Path.GetTempPath(), "pigeon-*.old.exe");
        if (folder is not null)
            old = old.Concat(Directory.EnumerateFiles(folder, "pigeon.exe.*.old"));
        foreach (var file in old.ToList()) {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task InstallAsync(string file, SemVersion version) {
        switch (Kind) {
            case InstallKind.Msi:
                await InstallMsiAsync(file);
                break;
            case InstallKind.Pkg:
                await Elevated(file, "installer", "-pkg", file, "-target", "/");
                break;
            case InstallKind.Deb when File.Exists("/usr/bin/apt-get"):
                await Elevated(file, "apt-get", "install", "-y", file);
                break;
            case InstallKind.Deb:
                await Elevated(file, "dpkg", "-i", file);
                break;
            case InstallKind.Rpm when File.Exists("/usr/bin/dnf"):
                await Elevated(file, "dnf", "install", "-y", file);
                break;
            case InstallKind.Rpm:
                await Elevated(file, "rpm", "-U", file);
                break;
            case InstallKind.Tarball:
                ReplaceFromTarball(file);
                break;
            default:
                throw new CommandException(Reason);
        }
    }

    // Windows lets a running program be moved but not replaced: this pigeon.exe, and the ones agent
    // apps run, move out of the install folder so the installer can put the new one in place and
    // the old product's removal finds nothing of it there.
    private async Task InstallMsiAsync(string file) {
        var aside = MoveAside();
        var log = Path.Combine(Path.GetTempPath(), "pigeon-update.log");
        Console.WriteLine("Running the Windows installer...");
        var code = await Updater.RunProgramAsync("msiexec", "/i", file, "/qn", "/norestart", "/l*v", log);
        if (code is 0 or 3010 && File.Exists(Executable))
            return;
        // The old version's removal may have taken the install folder with it.
        var restored = File.Exists(Executable);
        if (!restored) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Executable)!);
                File.Move(aside, Executable);
                restored = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        throw new CommandException(code is 0 or 3010
            ? $"The Windows installer finished but did not put pigeon.exe in {Path.GetDirectoryName(Executable)}. Its log is {log}."
            : restored
                ? $"The Windows installer failed (exit code {code}); Pigeon was not changed. Its log is {log}."
                : $"The Windows installer failed (exit code {code}), and Pigeon could not be put back: copy {aside} to {Executable}. The installer's log is {log}.");
    }

    private string MoveAside() {
        var tag = $"{Updater.CurrentVersion()}-{Environment.ProcessId}";
        var temp = Path.Combine(Path.GetTempPath(), $"pigeon-{tag}.old.exe");
        try {
            File.Move(Executable, temp);
            return temp;
        }
        catch (IOException) {
            // The temp folder is on another drive: the move copied the file but could not delete
            // the running original.
            try { File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var beside = $"{Executable}.{tag}.old";
        File.Move(Executable, beside);
        return beside;
    }

    // The package managers need administrator rights; sudo asks for the password in this terminal.
    private static async Task Elevated(string file, string program, params string[] arguments) {
        Console.WriteLine($"Installing needs administrator rights: sudo {program} {string.Join(' ', arguments)}");
        var code = await Updater.RunProgramAsync("sudo", [program, .. arguments]);
        if (code != 0)
            throw new CommandException($"Installing {Path.GetFileName(file)} failed (exit code {code}).");
    }

    private void ReplaceFromTarball(string file) {
        var folder = Path.GetDirectoryName(Executable) ?? throw new CommandException("Cannot find where Pigeon is installed.");
        var next = Path.Combine(folder, $".pigeon-update-{Environment.ProcessId}");
        try {
            using (var gzip = new GZipStream(File.OpenRead(file), CompressionMode.Decompress))
            using (var tar = new TarReader(gzip)) {
                TarEntry? entry;
                while ((entry = tar.GetNextEntry()) is not null) {
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || Path.GetFileName(entry.Name) != "pigeon")
                        continue;
                    entry.ExtractToFile(next, overwrite: true);
                    break;
                }
            }

            if (!File.Exists(next))
                throw new CommandException($"{Path.GetFileName(file)} holds no pigeon program.");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(next, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            // A rename replaces the file atomically; programs still running the old one keep it.
            File.Move(next, Executable, overwrite: true);
        }
        catch (UnauthorizedAccessException) {
            throw new CommandException($"This user cannot change {folder}. Update Pigeon there yourself, or move it to a folder you own.");
        }
        finally {
            if (File.Exists(next))
                File.Delete(next);
        }
    }
}
