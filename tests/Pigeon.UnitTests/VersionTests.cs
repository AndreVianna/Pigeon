using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Pigeon.UnitTests;

// The CLI and both plugins are versioned in lockstep: bump all three together.
public class VersionTests {
    private static readonly string Root = FindRoot();

    private static string FindRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pigeon.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string CliVersion() =>
        XDocument.Load(Path.Combine(Root, "src", "Pigeon.Cli", "Pigeon.Cli.csproj"))
            .Descendants("Version").Single().Value;

    private static string PluginVersion(params string[] manifest) {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine([Root, .. manifest])));
        return json.RootElement.GetProperty("version").GetString()!;
    }

    [Fact]
    public void The_Claude_plugin_has_the_CLI_version() =>
        Assert.Equal(CliVersion(), PluginVersion("integrations", "claude-plugin", ".claude-plugin", "plugin.json"));

    [Fact]
    public void The_Codex_plugin_has_the_CLI_version() =>
        Assert.Equal(CliVersion(), PluginVersion("integrations", "codex-plugin", ".codex-plugin", "plugin.json"));
}
