using System.Diagnostics;

using Reqnroll;

using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "claude_wrapper")]
public sealed class ClaudeHookWrapperSteps {
    private readonly string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "pigeon-wrapper-" + Guid.NewGuid().ToString("N"));
    private string sh = "", wrapper = "", fake = "";

    [Given("the production Claude hook wrapper and an isolated fake CLI")]
    public void Setup() {
        // The plugin's hooks run the wrapper as `sh`, which is dash on Debian and Ubuntu.
        sh = TestTools.Sh;
        Assert.True(File.Exists(sh), $"{sh} is required to test the Claude hook wrapper; on Windows it comes with Git Bash (see PIGEON_TEST_BASH).");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pigeon.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        wrapper = Path.Combine(root.FullName, "integrations", "claude-plugin", "scripts", "hook-wait.sh");
        Directory.CreateDirectory(directory);
        fake = Path.Combine(directory, OperatingSystem.IsWindows() ? "fake.cmd" : "fake.sh");
        if (OperatingSystem.IsWindows()) {
            // A second Git Bash process warns about /tmp on minimal Windows installations.
            // A batch fake exercises the wrapper without adding that unrelated stderr line.
            File.WriteAllText(fake, """
                @echo off
                if "%PIGEON_TEST_MODE%"=="wake" (
                  echo Pigeon wake generation 0123456789abcdef0123456789abcdef. Synthetic wake 1>&2
                  exit /b 2
                )
                if "%PIGEON_TEST_MODE%"=="renew" (
                  echo Pigeon notification waiter needs renewal. Synthetic renewal 1>&2
                  exit /b 2
                )
                if "%PIGEON_TEST_MODE%"=="crlf" (
                  echo Pigeon notification waiter needs renewal. Synthetic renewal 1>&2
                  exit /b 2
                )
                if "%PIGEON_TEST_MODE%"=="invalid" (
                  echo Error: invalid arguments 1>&2
                  exit /b 2
                )
                if "%PIGEON_TEST_MODE%"=="multiline" (
                  echo Pigeon notification waiter needs renewal. Synthetic renewal 1>&2
                  echo unexpected text 1>&2
                  exit /b 2
                )
                if "%PIGEON_TEST_MODE%"=="failure" (
                  echo Pigeon notification waiter needs renewal. Synthetic renewal 1>&2
                  exit /b 1
                )
                exit /b 0
                """.ReplaceLineEndings("\r\n"));
        } else {
            File.WriteAllText(fake, """
            #!/bin/sh
            case "$PIGEON_TEST_MODE" in
              wake) printf '%s\n' 'Pigeon wake generation 0123456789abcdef0123456789abcdef. Synthetic wake' >&2; exit 2 ;;
              renew) printf '%s\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' >&2; exit 2 ;;
              crlf) printf '%s\r\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' >&2; exit 2 ;;
              invalid) printf '%s\n' 'Error: invalid arguments' >&2; exit 2 ;;
              multiline) printf '%s\n%s\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' 'unexpected text' >&2; exit 2 ;;
              failure) printf '%s\n' 'Pigeon notification waiter needs renewal. Synthetic renewal' >&2; exit 1 ;;
            esac
            exit 0
            """.ReplaceLineEndings("\n"));
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Then("valid wakes and renewals pass through and CLI errors remain silent")]
    public async Task Verify() {
        foreach (var mode in new[] { "wake", "renew", "crlf", "invalid", "multiline", "failure", "empty" }) {
            var start = new ProcessStartInfo(sh) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(wrapper);
            foreach (var value in new[] { "--timeout", "43200", "--renew" })
                start.ArgumentList.Add(value);
            start.Environment["PIGEON_EXE"] = fake;
            start.Environment["PIGEON_TEST_MODE"] = mode;
            using var process = Process.Start(start)!;
            await process.StandardInput.WriteAsync("{}");
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var valid = mode is "wake" or "renew" or "crlf";
            var text = (await errors).ReplaceLineEndings("\n");
            const string missingGitTemp = "bash.exe: warning: could not find /tmp, please create!\n";
            if (OperatingSystem.IsWindows() && text.StartsWith(missingGitTemp, StringComparison.Ordinal))
                text = text[missingGitTemp.Length..];
            Assert.True(process.ExitCode == (valid ? 2 : 0), $"Mode {mode} exited {process.ExitCode}; stdout: {await output}; stderr: {text}");
            Assert.Equal("", await output);
            if (valid)
                Assert.StartsWith(mode == "wake" ? "Pigeon wake generation " : "Pigeon notification waiter needs renewal. ", text);
            else
                Assert.Equal("", text);
        }
    }

    [AfterScenario]
    public void Cleanup() {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
