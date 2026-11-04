using System.Text.Json;
using System.Xml.Linq;

using Xunit;

namespace Pigeon.UnitTests;

// The CLI and the plugins are versioned in lockstep: bump them all together.
public class VersionTests {
    private static readonly string Root = FindRoot();

    private static string FindRoot() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pigeon.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string CliVersion()
        => XDocument.Load(Path.Combine(Root, "src", "Pigeon.Cli", "Pigeon.Cli.csproj"))
            .Descendants("Version").Single().Value;

    private static string PluginVersion(params string[] manifest) {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine([Root, .. manifest])));
        return json.RootElement.GetProperty("version").GetString()!;
    }

    [Fact]
    public void The_Claude_plugin_has_the_CLI_version()
        => Assert.Equal(CliVersion(), PluginVersion("integrations", "claude-plugin", ".claude-plugin", "plugin.json"));

    [Fact]
    public void The_Codex_plugin_has_the_CLI_version()
        => Assert.Equal(CliVersion(), PluginVersion("integrations", "codex-plugin", ".codex-plugin", "plugin.json"));

    [Fact]
    public void The_OpenCode_plugin_has_the_CLI_version() {
        var source = File.ReadAllText(Path.Combine(Root, "integrations", "opencode-plugin", "pigeon", "waker.ts"));
        Assert.Contains($"export const version = \"{CliVersion()}\"", source);
    }

    [Fact]
    public void The_Copilot_plugin_has_the_CLI_version() {
        Assert.Equal(CliVersion(), PluginVersion("integrations", "copilot-plugin", "plugin.json"));
        var waker = File.ReadAllText(Path.Combine(Root, "integrations", "copilot-plugin", "extensions", "pigeon", "waker.mjs"));
        Assert.Contains($"export const version = \"{CliVersion()}\"", waker);
    }

    [Fact]
    public void The_Antigravity_plugin_has_the_CLI_version()
        => Assert.Equal(CliVersion(), PluginVersion("integrations", "antigravity-plugin", "plugin.json"));
}
