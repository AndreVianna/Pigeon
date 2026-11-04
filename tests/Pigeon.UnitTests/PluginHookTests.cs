using System.Text.Json;

using Xunit;

namespace Pigeon.UnitTests;

// Cursor imports installed Claude plugins' hooks and runs them through PowerShell, where a bare
// script path opens the script in its associated editor. Running the wrapper through `sh` works in
// Claude Code's Git Bash and fails harmlessly elsewhere; `bash` would start WSL on many machines.
public class PluginHookTests {
    private static string Root() {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Pigeon.slnx")))
                return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_Claude_plugin_runs_its_hook_wrapper_through_sh() {
        using var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "integrations", "claude-plugin", "hooks", "hooks.json")));
        var commands = hooks.RootElement.GetProperty("hooks").EnumerateObject()
            .SelectMany(e => e.Value.EnumerateArray()).SelectMany(group => group.GetProperty("hooks").EnumerateArray())
            .Where(hook => hook.GetProperty("type").GetString() == "command")
            .Select(hook => hook.GetProperty("command").GetString()!).ToArray();
        Assert.Equal(2, commands.Length);
        Assert.All(commands, command => Assert.StartsWith("sh \"${CLAUDE_PLUGIN_ROOT}/scripts/hook-wait.sh\" ", command));
    }

    // The OpenCode plugin is TypeScript, so its copy of the wake text is checked against the CLI's.
    [Fact]
    public void The_OpenCode_plugin_sends_the_CLI_wake_text_as_a_queued_prompt_and_registers_the_MCP_server() {
        var plugin = Path.Combine(Root(), "integrations", "opencode-plugin", "pigeon");
        var waker = File.ReadAllText(Path.Combine(plugin, "waker.ts"));
        var start = waker.IndexOf("`Pigeon wake generation ", StringComparison.Ordinal);
        var template = waker[(start + 1)..waker.IndexOf('`', start + 1)];
        Assert.Equal(Pigeon.Adapters.WakePrompt.For("GEN"), template.Replace("${generation}", "GEN"));
        var server = File.ReadAllText(Path.Combine(plugin, "server.ts"));
        Assert.Contains("command: [\"pigeon\", \"mcp\"]", server);
        Assert.Contains("PIGEON_RUNTIME: \"opencode\"", server);
        Assert.Contains("delivery: \"queue\"", server);
    }

    // The Copilot extension is JavaScript, so its copy of the wake text is checked against the CLI's.
    [Fact]
    public void The_Copilot_plugin_sends_the_CLI_wake_text_as_a_queued_prompt_and_registers_the_MCP_server() {
        var plugin = Path.Combine(Root(), "integrations", "copilot-plugin");
        var waker = File.ReadAllText(Path.Combine(plugin, "extensions", "pigeon", "waker.mjs"));
        var start = waker.IndexOf("`Pigeon wake generation ", StringComparison.Ordinal);
        var template = waker[(start + 1)..waker.IndexOf('`', start + 1)];
        Assert.Equal(Pigeon.Adapters.WakePrompt.For("GEN"), template.Replace("${generation}", "GEN"));
        var extension = File.ReadAllText(Path.Combine(plugin, "extensions", "pigeon", "extension.mjs"));
        Assert.Contains("mode: \"enqueue\"", extension);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(plugin, "plugin.json")));
        Assert.Equal(".mcp.json", manifest.RootElement.GetProperty("mcpServers").GetString());
        Assert.Equal("extensions", manifest.RootElement.GetProperty("extensions").GetString());
        using var mcp = JsonDocument.Parse(File.ReadAllText(Path.Combine(plugin, ".mcp.json")));
        var server = mcp.RootElement.GetProperty("mcpServers").GetProperty("pigeon");
        Assert.Equal(["mcp"], server.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal("copilot", server.GetProperty("env").GetProperty("PIGEON_RUNTIME").GetString());
    }

    // Antigravity runs hook commands through cmd on Windows, where a quoted path breaks the
    // command silently, so each hook is a bare `pigeon` command the CLI accepts.
    [Fact]
    public void The_Antigravity_plugin_reports_turns_with_bare_pigeon_commands() {
        var plugin = Path.Combine(Root(), "integrations", "antigravity-plugin");
        using var hooks = JsonDocument.Parse(File.ReadAllText(Path.Combine(plugin, "hooks.json")));
        var events = hooks.RootElement.GetProperty("pigeon").EnumerateObject()
            .ToDictionary(e => e.Name, e => e.Value.EnumerateArray().Select(h => h.GetProperty("command").GetString()!).Single());
        Assert.Equal(new Dictionary<string, string> {
            ["SessionStart"] = "pigeon hook session-start antigravity",
            ["PreInvocation"] = "pigeon hook turn-start antigravity",
            ["Stop"] = "pigeon hook turn-end antigravity"
        }, events);
        using var mcp = JsonDocument.Parse(File.ReadAllText(Path.Combine(plugin, "mcp_config.json")));
        Assert.Equal("antigravity", mcp.RootElement.GetProperty("mcpServers").GetProperty("pigeon").GetProperty("env").GetProperty("PIGEON_RUNTIME").GetString());
        using var sidecar = JsonDocument.Parse(File.ReadAllText(Path.Combine(plugin, "sidecars", "wake", "sidecar.json")));
        Assert.Equal(["sidecar", "antigravity"], sidecar.RootElement.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
    }
}
