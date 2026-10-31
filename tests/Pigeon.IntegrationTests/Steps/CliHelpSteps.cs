using System.Diagnostics;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "cli_help")]
public sealed class CliHelpSteps {
    private int exitCode;
    private string stdout = "", stderr = "";

    [When("I run Pigeon with arguments {string}")]
    public async Task Run(string arguments) {
        var output = Path.GetDirectoryName(typeof(CliHelpSteps).Assembly.Location)!;
        var info = new ProcessStartInfo(Path.Combine(output, "pigeon.exe")) {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.Environment.Remove("PIGEON_PROJECT");
        info.Environment.Remove("PIGEON_AGENT");
        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Pigeon");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            stdout = await outputTask;
            stderr = await errorTask;
            exitCode = process.ExitCode;
        }
        finally {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Then("the command succeeds and writes help to standard output")]
    public void HelpSucceeded() {
        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: pigeon", stdout);
        Assert.Equal("", stderr);
    }

    [Then("the help lists the service commands without a demo")]
    public void ListsCommands() {
        Assert.DoesNotContain("  adapters", stdout);
        Assert.Contains("Codex queue notifications require explicit opt-in", stdout);
        Assert.Contains("Claude can receive notifications", stdout);
        Assert.Contains("start", stdout);
        Assert.DoesNotContain("  serve", stdout);
        Assert.Contains("mcp", stdout);
        Assert.Contains("status", stdout);
        Assert.DoesNotContain("  pause", stdout);
        Assert.DoesNotContain("demo", stdout);
        Assert.DoesNotContain("experimental-", stdout);
        Assert.Contains("--help", stdout);
        Assert.Contains("--version", stdout);
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
}
