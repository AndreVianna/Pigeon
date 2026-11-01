using System.Diagnostics;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "cli")]
public sealed class CliSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string port = TestPorts.FreePort();
    private bool serviceStarted;
    private int exitCode;
    private string stdout = "", stderr = "";

    private async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments) {
        var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["PIGEON_PORT"] = port;
        info.Environment["PIGEON_DATABASE"] = Path.Combine(directory, "messages.db");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Pigeon");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            return (process.ExitCode, await output, await error);
        }
        finally {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private async Task<string> Succeeds(params string[] arguments) {
        var (code, output, error) = await Run(arguments);
        Assert.True(code == 0, $"pigeon {string.Join(' ', arguments)} exited {code}: {error}");
        return output;
    }

    [When("I run Pigeon with arguments {string}")]
    public async Task RunArguments(string arguments) =>
        (exitCode, stdout, stderr) = await Run(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Then("the command succeeds and writes help to standard output")]
    public void HelpSucceeded() {
        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: pigeon", stdout);
        Assert.Equal("", stderr);
    }

    [Then("the help lists the commands people use and the ones used by the plugins")]
    public void ListsCommands() {
        foreach (var usage in new[] { "pigeon start", "pigeon stop", "pigeon status", "pigeon topics", "pigeon join <topic> as <session-id> using <tool>", "pigeon leave <topic> as <session-id>", "pigeon send from <session-id> to <topic>", "pigeon receive for <session-id> from <topic>", "Used by the plugins:", "pigeon mcp", "pigeon hook", "--version" })
            Assert.Contains(usage, stdout);
        foreach (var removed in new[] { "pigeon messages", "pigeon sessions", "subscribe", "pigeon ack", "pigeon get" })
            Assert.DoesNotContain(removed, stdout);
    }

    [Then("the help contains {string}")]
    public void ContainsUsage(string usage) => Assert.Contains(usage, stdout);

    [Then("the command succeeds and prints the package version")]
    public void VersionSucceeded() {
        Assert.Equal(0, exitCode);
        Assert.Matches(@"^\d+\.\d+\.\d+[^\r\n]*\s*$", stdout);
        Assert.Equal("", stderr);
    }

    [Then("the command reports a usage error on standard error")]
    public void UsageError() {
        Assert.Equal(2, exitCode);
        Assert.Equal("", stdout);
        Assert.Contains("Error:", stderr);
        Assert.Contains("--help", stderr);
    }

    [Given("an isolated background service")]
    public async Task StartService() {
        Directory.CreateDirectory(directory);
        Assert.Contains("Pigeon service started.", await Succeeds("start"));
        serviceStarted = true;
    }

    [When("two sessions join a topic and one sends a message")]
    public async Task Converse() {
        Assert.Equal("Joined parser-review. Waiting for a partner.", (await Succeeds("join", "parser-review", "as", "codex-1", "using", "codex")).Trim());
        Assert.Equal("Joined parser-review with codex codex-1.", (await Succeeds("join", "parser-review", "as", "claude-1", "using", "claude-code")).Trim());
        Assert.StartsWith("The recipient is unavailable right now", await Succeeds("send", "from", "codex-1", "to", "parser-review", "Ready for review"));
    }

    [When("the service is restarted")]
    public async Task Restart() {
        Assert.Contains("Pigeon service stopped.", await Succeeds("stop"));
        Assert.Equal(1, (await Run("status")).ExitCode);
        Assert.Contains("Pigeon service started.", await Succeeds("start"));
    }

    [Then("the topic still has both members and the other session receives the message once")]
    public async Task Survived() {
        Assert.Equal("parser-review: codex codex-1 and claude-code claude-1 | 1 waiting", (await Succeeds("topics")).Trim());
        var received = await Succeeds("receive", "for", "claude-1", "from", "parser-review");
        Assert.Matches(@"^\[\d{4}-\d\d-\d\d \d\d:\d\d:\d\dZ\] codex codex-1: Ready for review\s*$", received);
        Assert.Equal("No new messages.", (await Succeeds("receive", "for", "claude-1", "from", "parser-review")).Trim());
        Assert.Contains("Sessions with waiting messages: 0", await Succeeds("status"));
    }

    [Then("the last session to leave deletes the topic")]
    public async Task LastLeaves() {
        Assert.Equal("Left parser-review.", (await Succeeds("leave", "parser-review", "as", "codex-1")).Trim());
        Assert.Equal("Left parser-review. Nobody is left, so the topic was deleted.", (await Succeeds("leave", "parser-review", "as", "claude-1")).Trim());
        Assert.Equal("No topics.", (await Succeeds("topics")).Trim());
    }

    [Then("a third join, a send from a non-member and a receive from a non-member each fail with a clear message")]
    public async Task Refusals() {
        _ = await Succeeds("join", "parser-review", "as", "codex-1", "using", "codex");
        _ = await Succeeds("join", "parser-review", "as", "claude-1", "using", "claude-code");
        var full = await Run("join", "parser-review", "as", "codex-2", "using", "codex");
        Assert.Equal(1, full.ExitCode);
        Assert.StartsWith("Topic \"parser-review\" is full: codex codex-1 and claude-code claude-1 are in it.", full.Error);
        var send = await Run("send", "from", "codex-2", "to", "parser-review", "hello");
        Assert.Equal((1, "Session codex-2 is not in topic \"parser-review\"."), (send.ExitCode, send.Error.Trim()));
        var receive = await Run("receive", "for", "codex-2", "from", "parser-review");
        Assert.Equal(1, receive.ExitCode);
        Assert.Contains("is not in topic", receive.Error);
    }

    [When("I run a conversation command without a running service")]
    public async Task WithoutService() => (exitCode, stdout, stderr) = await Run("send", "from", "codex-1", "to", "parser-review", "hello");

    [Then("it fails with exit code 1 and suggests starting the service")]
    public void SuggestsStart() {
        Assert.Equal(1, exitCode);
        Assert.Contains($"The Pigeon service is not running on 127.0.0.1:{port}. Start it with 'pigeon start'.", stderr);
    }

    [AfterScenario]
    public async Task Cleanup() {
        if (serviceStarted) _ = await Run("stop");
        for (var i = 0; i < 20 && Directory.Exists(directory); i++) {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { await Task.Delay(200); }
        }
    }
}
