using System.Diagnostics;
using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "service_lifecycle")]
public sealed class ServiceLifecycleSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-lifecycle-" + Guid.NewGuid().ToString("N"));
    private readonly string pipe = TestPorts.FreePort();
    private readonly Principal principal = new("lifecycle-project", "operator");

    [BeforeScenario]
    public void Setup() => Directory.CreateDirectory(directory);

    [AfterScenario]
    public async Task Cleanup() {
        await StopIfRunning();
        for (var i = 0; i < 20 && await IsRunning(); i++)
            await Task.Delay(100);
        Assert.False(await IsRunning(), "The isolated service did not stop; its test directory was retained.");
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private async Task<bool> IsRunning() {
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var response = await LocalHttpClient.ExecuteAsync(pipe, principal, "service_status", new { }, timeout.Token);
            return response.GetProperty("Ok").GetBoolean();
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { return false; }
    }

    private async Task StopIfRunning() {
        if (await IsRunning()) {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            _ = await LocalHttpClient.ExecuteAsync(pipe, principal, "service_stop", new { }, timeout.Token);
        }
    }

    private async Task<(int ExitCode, string Output, string Error)> Run(params string[] arguments) {
        var output = Path.GetDirectoryName(typeof(ServiceLifecycleSteps).Assembly.Location)!;
        var info = new ProcessStartInfo(Path.Combine(output, "pigeon.exe")) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var arg in arguments)
            info.ArgumentList.Add(arg);
        info.Environment["PIGEON_PORT"] = pipe;
        info.Environment["PIGEON_DATABASE"] = Path.Combine(directory, "messages.db");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start test CLI");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
            return (process.ExitCode, await stdout, await stderr);
        }
        catch {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await StopIfRunning();
            throw;
        }
    }

    [When("I start an isolated background service")]
    public async Task Start() {
        var result = await Run("start");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("Pigeon service", result.Output);
    }

    [Then("the service reports that it is running")]
    public async Task Running() {
        var result = await Run("status");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("running", result.Output);
        Assert.Contains("MCP bridges: 0 recently active", result.Output);
    }

    [When("I create a topic in that service")]
    public async Task CreateTopic() => Assert.Equal(0, (await Run("topics", "create", "design")).ExitCode);

    [When("I stop the isolated background service")]
    public async Task Stop() {
        Assert.Equal(0, (await Run("stop")).ExitCode);
    }

    [Then("the service reports that it is unavailable")]
    public async Task Unavailable() {
        for (var i = 0; i < 20; i++) {
            var result = await Run("status");
            if (result.ExitCode != 0) {
                Assert.Contains("Pigeon service: unavailable", result.Error);
                return;
            }
            await Task.Delay(100);
        }
        throw new Xunit.Sdk.XunitException("The test service did not stop.");
    }

    [Then("the previously created topic is still available")]
    public async Task TopicRecovered() {
        var result = await Run("topics");
        Assert.Equal(0, result.ExitCode);
        using var topics = JsonDocument.Parse(result.Output);
        Assert.Contains(topics.RootElement.EnumerateArray(), item => item.GetString() == "design");
    }
}
