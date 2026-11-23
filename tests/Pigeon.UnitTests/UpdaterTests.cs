using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Xunit;

namespace Pigeon.UnitTests;

public class UpdaterTests {
    private static SemVersion V(string text) => SemVersion.Parse(text) ?? throw new ArgumentException(text);

    [Theory]
    [InlineData("0.7.0-beta.1", "0.7.0-beta.2")]
    [InlineData("0.7.0-beta.2", "0.7.0-beta.10")]
    [InlineData("0.7.0-alpha.8", "0.7.0-beta.1")]
    [InlineData("0.7.0-beta.10", "0.7.0")]
    [InlineData("0.7.0", "0.7.1-beta.1")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("0.7.0-beta", "0.7.0-beta.1")]
    [InlineData("0.7.0-1", "0.7.0-alpha")]
    public void Orders_versions_by_semver_precedence(string lower, string higher) {
        Assert.True(V(lower).CompareTo(V(higher)) < 0);
        Assert.True(V(higher).CompareTo(V(lower)) > 0);
    }

    [Theory]
    [InlineData("0.7.0-beta.1+abc123", "0.7.0-beta.1")]
    [InlineData("1.2.3", "1.2.3")]
    public void Ignores_build_metadata(string a, string b) => Assert.Equal(0, V(a).CompareTo(V(b)));

    [Theory]
    [InlineData("")]
    [InlineData("v0.7.0")]
    [InlineData("0.7")]
    [InlineData("0.7.0-")]
    [InlineData("0.7.0-beta..1")]
    [InlineData("latest")]
    public void Rejects_what_is_not_a_version(string text) => Assert.Null(SemVersion.Parse(text));

    // The releases as GitHub listed them when the update command was written: the newest stable
    // release (0.5.0) is older than the betas and ships only a NuGet package.
    private const string ReleasesJson = """
        [
          { "tag_name": "v0.8.0-beta.1", "draft": true, "prerelease": true, "assets": [] },
          { "tag_name": "v0.7.0-beta.1", "draft": false, "prerelease": true, "assets": [
              { "name": "Pigeon-0.7.0-beta.1-win-x64.msi", "size": 11000000, "browser_download_url": "https://github.com/AndreVianna/Pigeon/releases/download/v0.7.0-beta.1/Pigeon-0.7.0-beta.1-win-x64.msi" },
              { "name": "pigeon_0.7.0.beta.1_amd64.deb", "size": 11000000, "browser_download_url": "https://github.com/AndreVianna/Pigeon/releases/download/v0.7.0-beta.1/pigeon_0.7.0.beta.1_amd64.deb" },
              { "name": "SHA256SUMS.txt", "size": 900, "browser_download_url": "https://github.com/AndreVianna/Pigeon/releases/download/v0.7.0-beta.1/SHA256SUMS.txt" } ] },
          { "tag_name": "v0.6.0-beta.1", "draft": false, "prerelease": true, "assets": [] },
          { "tag_name": "nightly", "draft": false, "prerelease": true, "assets": [] },
          { "tag_name": "v0.5.0", "draft": false, "prerelease": false, "assets": [
              { "name": "Pigeon.Cli.0.5.0.nupkg", "size": 1000, "browser_download_url": "https://github.com/AndreVianna/Pigeon/releases/download/v0.5.0/Pigeon.Cli.0.5.0.nupkg" } ] }
        ]
        """;

    private static IReadOnlyList<Release> Releases() {
        using var json = JsonDocument.Parse(ReleasesJson);
        return Release.ParseAll(json.RootElement);
    }

    [Fact]
    public void Skips_drafts_and_tags_that_are_not_versions() {
        var releases = Releases();
        Assert.Equal(["v0.7.0-beta.1", "v0.6.0-beta.1", "v0.5.0"], releases.Select(r => r.Tag));
        Assert.Equal(3, releases[0].Assets.Count);
    }

    [Fact]
    public void Takes_the_newest_pre_release_only_when_allowed() {
        var choice = ReleaseChoice.Choose(Releases(), V("0.6.0-beta.1"), allowPreRelease: true);
        Assert.Equal("v0.7.0-beta.1", choice.Newest?.Tag);
    }

    [Fact]
    public void Never_moves_a_pre_release_back_to_an_older_stable_version() {
        var choice = ReleaseChoice.Choose(Releases(), V("0.7.0-beta.1"), allowPreRelease: false);
        Assert.Null(choice.Newest);
        Assert.Equal("v0.5.0", choice.NewestStable?.Tag);
    }

    [Fact]
    public void Reports_nothing_newer_when_the_newest_is_installed() {
        Assert.Null(ReleaseChoice.Choose(Releases(), V("0.7.0-beta.1"), allowPreRelease: true).Newest);
        Assert.Null(ReleaseChoice.Choose(Releases(), V("0.7.0-beta.2"), allowPreRelease: true).Newest);
    }

    [Fact]
    public void Takes_a_newer_stable_version_without_the_option() {
        var stable = new Release("v0.7.0", V("0.7.0"), false, []);
        var choice = ReleaseChoice.Choose([.. Releases(), stable], V("0.7.0-beta.1"), allowPreRelease: false);
        Assert.Same(stable, choice.Newest);
    }

    [Fact]
    public void A_release_marked_stable_with_a_pre_release_tag_counts_as_a_pre_release() {
        var odd = new Release("v0.9.0-rc.1", V("0.9.0-rc.1"), false, []);
        Assert.Null(ReleaseChoice.Choose([odd], V("0.7.0"), allowPreRelease: false).Newest);
        Assert.Same(odd, ReleaseChoice.Choose([odd], V("0.7.0"), allowPreRelease: true).Newest);
    }

    [Theory]
    [InlineData("0.7.0-beta.1", false, "newer than the newest stable version (0.5.0)")]
    [InlineData("0.5.0", false, "Pigeon 0.5.0 is the newest stable version.")]
    [InlineData("0.7.0-beta.1", true, "Pigeon 0.7.0-beta.1 is the newest version.")]
    public void Says_why_nothing_was_installed(string current, bool allowPreRelease, string expected) {
        var version = V(current);
        var choice = ReleaseChoice.Choose(Releases(), version, allowPreRelease);
        Assert.Null(choice.Newest);
        Assert.Contains(expected, Updater.UpToDate(version, choice, allowPreRelease), StringComparison.Ordinal);
    }

    [Fact]
    public void Loads_every_assembly_Pigeon_references() => Assert.True(Updater.LoadAllAssemblies() > 5);

    [Fact]
    public void Finds_a_checksum_including_deb_names_that_GitHub_renamed() {
        const string sums = """
            6837443a95edc457c82f04c3f199a1ff464c2822aab344e54da74a2822336a7b  Pigeon-0.7.0-beta.1-win-x64.msi
            7A33C74D4691866A5CEB9589664460E1668E9620A67CBCC142E9137595D6EF59  pigeon_0.7.0~beta.1_amd64.deb
            not a line
            """;
        Assert.Equal("6837443a95edc457c82f04c3f199a1ff464c2822aab344e54da74a2822336a7b", Checksums.Find(sums, "Pigeon-0.7.0-beta.1-win-x64.msi"));
        Assert.Equal("7a33c74d4691866a5ceb9589664460e1668e9620a67cbcc142e9137595d6ef59", Checksums.Find(sums, "pigeon_0.7.0.beta.1_amd64.deb"));
        Assert.Null(Checksums.Find(sums, "pigeon-0.7.0-beta.1-linux-x64.tar.gz"));
    }

    [Theory]
    [InlineData(Architecture.X64, "_amd64.deb", ".x86_64.rpm", "-linux-x64.tar.gz")]
    [InlineData(Architecture.Arm64, "_arm64.deb", ".aarch64.rpm", "-linux-arm64.tar.gz")]
    public void Picks_the_Linux_file_by_how_Pigeon_was_installed(Architecture arch, string deb, string rpm, string tar) {
        Assert.Equal((InstallKind.Deb, deb), Kind(InstallInfo.Detect("linux", "/usr/bin/pigeon", arch, null, p => p == "deb")));
        Assert.Equal((InstallKind.Rpm, rpm), Kind(InstallInfo.Detect("linux", "/usr/bin/pigeon", arch, null, p => p == "rpm")));
        Assert.Equal((InstallKind.Tarball, tar), Kind(InstallInfo.Detect("linux", "/home/ana/bin/pigeon", arch, null, _ => false)));
    }

    [Fact]
    public void Picks_the_Mac_package_for_the_processor_only_when_the_package_installed_Pigeon() {
        Assert.Equal((InstallKind.Pkg, "-osx-arm64.pkg"), Kind(InstallInfo.Detect("macos", "/usr/local/bin/pigeon", Architecture.Arm64, null, p => p == "pkg")));
        Assert.Equal((InstallKind.Pkg, "-osx-x64.pkg"), Kind(InstallInfo.Detect("macos", "/usr/local/bin/pigeon", Architecture.X64, null, p => p == "pkg")));
        Assert.Equal(InstallKind.Unsupported, InstallInfo.Detect("macos", "/Users/ana/pigeon", Architecture.Arm64, null, _ => true).Kind);
        Assert.Equal(InstallKind.Unsupported, InstallInfo.Detect("macos", "/usr/local/bin/pigeon", Architecture.Arm64, null, _ => false).Kind);
    }

    [Fact]
    public void Updates_on_Windows_only_the_copy_the_installer_put_there() {
        const string exe = @"C:\Users\ana\AppData\Local\Programs\Pigeon\pigeon.exe";
        Assert.Equal((InstallKind.Msi, "-win-x64.msi"), Kind(InstallInfo.Detect("windows", exe, Architecture.X64, @"C:\Users\ana\AppData\Local\Programs\Pigeon\", _ => false)));
        Assert.Equal(InstallKind.Unsupported, InstallInfo.Detect("windows", @"C:\tools\pigeon.exe", Architecture.X64, @"C:\Users\ana\AppData\Local\Programs\Pigeon\", _ => false).Kind);
        Assert.Equal(InstallKind.Unsupported, InstallInfo.Detect("windows", exe, Architecture.X64, null, _ => false).Kind);
    }

    private static (InstallKind, string) Kind(InstallInfo info) => (info.Kind, info.AssetSuffix);

    private sealed class Server(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    [Fact]
    public async Task Asks_GitHub_for_the_releases_with_a_user_agent() {
        using var server = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ReleasesJson) });
        using var http = new HttpClient(server);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("pigeon/test");
        var releases = await Updater.FetchReleasesAsync(http);
        Assert.Equal(3, releases.Count);
        Assert.Equal("api.github.com", server.Requests.Single().RequestUri?.Host);
    }

    [Fact]
    public async Task Explains_GitHub_rate_limits() {
        using var server = new Server(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var http = new HttpClient(server);
        var error = await Assert.ThrowsAsync<CommandException>(() => Updater.FetchReleasesAsync(http));
        Assert.Contains("too many", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_a_download_only_when_it_matches_its_checksum() {
        var body = Encoding.UTF8.GetBytes("installer bytes");
        var sha = Convert.ToHexStringLower(SHA256.HashData(body));
        using var server = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        using var http = new HttpClient(server);
        var asset = new ReleaseAsset("pigeon-0.7.0-beta.2-linux-x64.tar.gz", Updater.DownloadPrefix + "v0.7.0-beta.2/pigeon-0.7.0-beta.2-linux-x64.tar.gz", body.Length);
        var folder = Directory.CreateTempSubdirectory("pigeon-updater-test-");
        try {
            var path = Path.Combine(folder.FullName, asset.Name);
            await Updater.DownloadAsync(http, asset, path, sha);
            Assert.Equal(body, await File.ReadAllBytesAsync(path));

            var other = Path.Combine(folder.FullName, "other");
            var error = await Assert.ThrowsAsync<CommandException>(() => Updater.DownloadAsync(http, asset, other, new string('0', 64)));
            Assert.Contains("SHA256", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(other));
        }
        finally {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Downloads_only_from_Pigeons_releases() {
        using var server = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(server);
        var asset = new ReleaseAsset("pigeon.tar.gz", "https://example.com/pigeon.tar.gz", 1);
        _ = await Assert.ThrowsAsync<CommandException>(() => Updater.DownloadAsync(http, asset, Path.Combine(Path.GetTempPath(), "never-written"), new string('0', 64)));
        Assert.Empty(server.Requests);
    }
}
