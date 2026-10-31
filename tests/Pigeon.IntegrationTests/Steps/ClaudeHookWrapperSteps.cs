using System.Diagnostics;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "claude_wrapper")]
public sealed class ClaudeHookWrapperSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-wrapper-" + Guid.NewGuid().ToString("N"));
    private string bash = "", wrapper = "", fake = "";

    [Given("the production Claude hook wrapper and an isolated fake CLI")]
    public void Setup() {
        bash = Environment.GetEnvironmentVariable("PIGEON_TEST_BASH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        Assert.True(File.Exists(bash), "Git Bash is required to test the Claude hook wrapper; set PIGEON_TEST_BASH to its executable.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pigeon.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        wrapper = Path.Combine(root.FullName, "integrations", "claude-plugin", "scripts", "hook-wait.sh");
        Directory.CreateDirectory(directory);
        fake = Path.Combine(directory, "fake.sh");
        File.WriteAllText(fake, """
            #!/bin/bash
            case "$PIGEON_TEST_MODE" in
              wake) printf '%s\n' 'Pigeon wake generation 0123456789abcdef0123456789abcdef. Synthetic wake' >&2; exit 2 ;;
              renew) printf '%s\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' >&2; exit 2 ;;
              invalid) printf '%s\n' 'Error: invalid arguments' >&2; exit 2 ;;
              multiline) printf '%s\n%s\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' 'unexpected text' >&2; exit 2 ;;
              failure) printf '%s\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' >&2; exit 1 ;;
            esac
            exit 0
            """);
    }

    [Then("valid wakes and renewals pass through and CLI errors remain silent")]
    public async Task Verify() {
        foreach (var mode in new[] { "wake", "renew", "invalid", "multiline", "failure", "empty" }) {
            var start = new ProcessStartInfo(bash) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(wrapper);
            foreach (var value in new[] { "--timeout", "43200", "--renew" }) start.ArgumentList.Add(value);
            start.Environment["PIGEON_EXE"] = fake;
            start.Environment["PIGEON_TEST_MODE"] = mode;
            using var process = Process.Start(start)!;
            await process.StandardInput.WriteAsync("{}");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var valid = mode is "wake" or "renew";
            Assert.Equal(valid ? 2 : 0, process.ExitCode);
            Assert.Equal("", await output);
            var text = await errors;
            if (valid) Assert.StartsWith(mode == "wake" ? "Pigeon wake generation " : "Pigeon notification waiter needs renewal. ", text);
            else Assert.Equal("", text);
        }
    }

    [AfterScenario]
    public void Cleanup() {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
