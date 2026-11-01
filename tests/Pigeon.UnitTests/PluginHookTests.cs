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
}
