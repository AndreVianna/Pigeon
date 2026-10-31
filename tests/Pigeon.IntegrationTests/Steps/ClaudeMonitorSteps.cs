using System.Diagnostics;
using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "claude_monitor")]
public sealed class ClaudeMonitorSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-monitor-" + Guid.NewGuid().ToString("N"));
    private readonly string port = TestPorts.FreePort();
    private readonly Principal sender = new(Mailbox.TopicNamespace, "sender");
    private readonly Principal reader = new(Mailbox.TopicNamespace, "reader");
    private Broker broker = null!;
    private CancellationTokenSource stop = null!;
    private Task server = null!;
    private Process monitor = null!;
    private Task<string?> line = null!;
    private Process? launcher, orphanCheck;

    private async Task<JsonElement> Call(Principal principal, string operation, object args) {
        var result = await LocalHttpClient.ExecuteAsync(port, principal, operation, args);
        Assert.True(result.GetProperty("Ok").GetBoolean(), result.ToString());
        return result.GetProperty("Value");
    }

    [Given("an isolated service and a Claude monitor for a registered session")]
    public async Task Setup() {
        Directory.CreateDirectory(directory);
        broker = new Broker(Path.Combine(directory, "messages.db"), topicMode: true);
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        await Call(sender, "register", new { runtime = "codex", sessionId = "sender-session" });
        await Call(reader, "register", new { runtime = "claude-code", sessionId = "reader-session" });
        await Call(sender, "topic_create", new { topic = "test" });
        await Call(reader, "subscribe", new { topic = "test" });
        var output = Path.GetDirectoryName(typeof(ClaudeMonitorSteps).Assembly.Location)!;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.Combine(output, "Pigeon.IntegrationTests.runtimeconfig.json"), "--depsfile", Path.Combine(output, "Pigeon.IntegrationTests.deps.json"), Path.Combine(output, "pigeon.dll"), "messages", "wait", "--claude" }) start.ArgumentList.Add(arg);
        start.Environment["PIGEON_PORT"] = port;
        start.Environment["CLAUDE_CODE_SESSION_ID"] = "reader-session";
        monitor = Process.Start(start)!;
    }

    [When("messages arrive while that session's bridge is unavailable")]
    public async Task Publish() {
        line = monitor.StandardOutput.ReadLineAsync();
        foreach (var key in new[] { "one", "two" })
            await Call(sender, "publish", new { topic = "test", body = "synthetic", correlationId = "test", idempotencyKey = key, expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() });
        var wrong = new Principal(Mailbox.TopicNamespace, "other-session");
        await Call(wrong, "register", new { runtime = "claude-code", sessionId = "other-session" });
        await Call(wrong, "bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "mcp" });
        await Task.Delay(1800);
        Assert.False(line.IsCompleted);
    }

    [Then("the monitor waits until the matching bridge connects and emits a single wake without leasing messages")]
    public async Task Wakes() {
        var bridgeId = Guid.NewGuid().ToString("N");
        await Call(reader, "bridge_heartbeat", new { id = bridgeId, mode = "mcp" });
        var output = await line.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.StartsWith("Pigeon wake generation ", output);
        Assert.DoesNotContain("synthetic", output);
        var generation = output!.Split(' ')[3].TrimEnd('.');
        var status = await Call(reader, "wake_status", new { });
        Assert.Equal(2, status.GetProperty("Queued").GetInt32());
        Assert.Equal(0, status.GetProperty("Leased").GetInt32());
        Assert.Equal(1, status.GetProperty("Attempts").GetInt32());
        Assert.Equal("claude-monitor", status.GetProperty("History")[0].GetProperty("Mode").GetString());
        var confirm = await Call(reader, "wake_confirm", new { generation });
        Assert.True(confirm.GetProperty("Accepted").GetBoolean());
        for (var i = 0; i < 2; i++) {
            var message = await Call(reader, "receive", new { });
            await Call(reader, "acknowledge", new { receipt = message.GetProperty("Receipt").GetString() });
        }
        await Call(reader, "wake_complete", new { generation });
        Assert.Equal("completed", (await Call(reader, "wake_status", new { })).GetProperty("State").GetString());
        // The same monitor survives a service restart and waits for the bridge to reconnect.
        line = monitor.StandardOutput.ReadLineAsync();
        stop.Cancel();
        await server;
        stop.Dispose();
        broker.Dispose();
        await Task.Delay(1300);
        broker = new Broker(Path.Combine(directory, "messages.db"), topicMode: true);
        stop = new CancellationTokenSource();
        var restarted = new LocalHttpServer(port, broker);
        server = restarted.RunAsync(stop.Token);
        await restarted.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        await Call(sender, "publish", new { topic = "test", body = "after restart", correlationId = "test", idempotencyKey = "three", expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() });
        await Task.Delay(1300);
        Assert.False(line.IsCompleted);
        await Call(reader, "bridge_heartbeat", new { id = bridgeId, mode = "mcp" });
        var afterRestart = await line.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.StartsWith("Pigeon wake generation ", afterRestart);
        Assert.NotEqual(output, afterRestart);
        Assert.Equal(1, (await Call(reader, "wake_status", new { })).GetProperty("Queued").GetInt32());
    }

    [Then("an orphaned monitor exits without claiming a later message")]
    public async Task ParentExit() {
        monitor.Kill();
        await monitor.WaitForExitAsync();
        monitor.Dispose();
        monitor = null!;
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        var output = Path.GetDirectoryName(typeof(ClaudeMonitorSteps).Assembly.Location)!;
        var args = new[] { "exec", "--runtimeconfig", Path.Combine(output, "Pigeon.IntegrationTests.runtimeconfig.json"), "--depsfile", Path.Combine(output, "Pigeon.IntegrationTests.deps.json"), Path.Combine(output, "pigeon.dll"), "messages", "wait", "reader", "reader-session" };
        var script = "$child = Start-Process -FilePath 'dotnet' -ArgumentList @(" + string.Join(",", args.Select(Quote)) + ") -PassThru -WindowStyle Hidden -RedirectStandardOutput " + Quote(Path.Combine(directory, "orphan-out.txt")) + " -RedirectStandardError " + Quote(Path.Combine(directory, "orphan-err.txt")) + "; Write-Output $child.Id; [Console]::In.ReadLine() | Out-Null";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", script }) start.ArgumentList.Add(arg);
        start.Environment["PIGEON_PORT"] = port;
        launcher = Process.Start(start)!;
        var pid = int.Parse((await launcher.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!);
        orphanCheck = Process.GetProcessById(pid);
        var bridgeId = Guid.NewGuid().ToString("N");
        await Call(reader, "bridge_heartbeat", new { id = bridgeId, mode = "mcp" });
        var connected = false;
        for (var i = 0; i < 15 && !connected; i++) {
            var bridges = await Call(sender, "bridges", new { });
            connected = bridges.EnumerateArray().Any(b => b.GetProperty("mode").GetString() == "claude-monitor" && b.GetProperty("agent").GetString() == "reader");
            if (!connected) await Task.Delay(500);
        }
        Assert.True(connected, "The test monitor did not start before its launcher exited.");
        launcher.StandardInput.Close();
        await launcher.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await orphanCheck.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Call(sender, "publish", new { topic = "test", body = "after launcher exit", correlationId = "test", idempotencyKey = "after-exit", expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() });
        await Task.Delay(1500);
        var wake = await Call(reader, "wake_status", new { });
        Assert.Equal("ready", wake.GetProperty("State").GetString());
        Assert.Equal(0, wake.GetProperty("Attempts").GetInt32());
    }

    [Then("a broken output pipe does not cause repeated wake claims")]
    public async Task BrokenOutput() {
        var bridgeId = Guid.NewGuid().ToString("N");
        await Call(reader, "bridge_heartbeat", new { id = bridgeId, mode = "mcp" });
        var connected = false;
        for (var i = 0; i < 15 && !connected; i++) {
            var bridges = await Call(sender, "bridges", new { });
            connected = bridges.EnumerateArray().Any(b => b.GetProperty("mode").GetString() == "claude-monitor" && b.GetProperty("agent").GetString() == "reader");
            if (!connected) await Task.Delay(500);
        }
        Assert.True(connected);
        // Close the read end before any wake is produced.
        monitor.StandardOutput.Close();
        await Call(sender, "publish", new { topic = "test", body = "broken output", correlationId = "test", idempotencyKey = "broken", expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() });
        await monitor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotEqual(0, monitor.ExitCode);
        var wake = await Call(reader, "wake_status", new { });
        Assert.Equal("failed", wake.GetProperty("State").GetString());
        Assert.Equal(1, wake.GetProperty("Attempts").GetInt32());
        Assert.Equal("transport-failed", wake.GetProperty("LastOutcome").GetString());
        Assert.Equal(1, wake.GetProperty("Queued").GetInt32());
        Assert.Equal(0, wake.GetProperty("Leased").GetInt32());
    }

    [AfterScenario]
    public async Task Cleanup() {
        if (launcher is not null) { if (!launcher.HasExited) launcher.Kill(); await launcher.WaitForExitAsync(); launcher.Dispose(); }
        if (orphanCheck is not null) { if (!orphanCheck.HasExited) orphanCheck.Kill(); await orphanCheck.WaitForExitAsync(); orphanCheck.Dispose(); }
        if (monitor is not null) { if (!monitor.HasExited) monitor.Kill(); await monitor.WaitForExitAsync(); monitor.Dispose(); }
        if (stop is not null) { stop.Cancel(); try { await server; } catch (OperationCanceledException) { } stop.Dispose(); }
        broker?.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
