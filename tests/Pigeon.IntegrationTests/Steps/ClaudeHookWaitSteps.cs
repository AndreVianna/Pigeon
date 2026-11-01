using System.Diagnostics;
using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "claude_hook")]
public sealed class ClaudeHookWaitSteps {
    private sealed record Waiter(Process Process, Task<string> Output, Task<string> Errors);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-hook-wait-" + Guid.NewGuid().ToString("N"));
    private readonly string port = TestPorts.FreePort();
    private const string Sender = "sender-session", Reader = "reader-session";
    private readonly string bridgeId = Guid.NewGuid().ToString("N");
    private readonly List<Process> processes = [];
    private Broker broker = null!;
    private CancellationTokenSource stop = null!;
    private Task server = null!;
    private CancellationTokenSource heartbeat = null!;
    private Task heartbeats = null!;
    private int key;

    private async Task<JsonElement> Call(string operation, object args) {
        var result = await LocalHttpClient.ExecuteAsync(port, operation, args);
        Assert.True(result.GetProperty("Ok").GetBoolean(), result.ToString());
        return result.GetProperty("Value");
    }

    private Task Publish(string body = "synthetic") =>
        Call("send", new { sessionId = Sender, topic = "test", message = body, retryKey = "key-" + ++key });
    private Task<JsonElement> WakeStatus() => Call("wake_status", new { sessionId = Reader });

    private async Task StartService() {
        broker = new Broker(Path.Combine(directory, "messages.db"));
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task StopService() {
        stop.Cancel();
        await server;
        stop.Dispose();
        broker.Dispose();
    }

    private Process Pigeon(params string[] args) {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        start.Environment["PIGEON_PORT"] = port;
        var process = Process.Start(start)!;
        processes.Add(process);
        return process;
    }

    private async Task<Waiter> StartWaiterWithInput(string input, params string[] extra) {
        var process = Pigeon(["hook", "wait", "claude-code", .. extra]);
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        // A waiter stops reading oversized input and may exit before the write finishes.
        try {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        catch (IOException) { }
        return new Waiter(process, output, errors);
    }

    private Task<Waiter> StartWaiter(string session = "reader-session", params string[] extra) =>
        StartWaiterWithInput(JsonSerializer.Serialize(new { session_id = session, hook_event_name = "Stop", prompt = "untrusted-extra-data" }), extra);

    private static async Task<int> Exit(Waiter waiter, int seconds = 10) {
        await waiter.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(seconds));
        return waiter.Process.ExitCode;
    }

    private async Task<JsonElement> Notifier() {
        var bridges = await Call("bridges", new { });
        return bridges.EnumerateArray().First(b => b.GetProperty("sessionId").GetString() == Reader && b.GetProperty("mode").GetString() == "mcp").GetProperty("notifier");
    }

    private async Task WaitForOwner(string mode) {
        for (var i = 0; i < 30; i++) {
            if ((await Notifier()).GetProperty("active").GetString() == mode) return;
            await Task.Delay(300);
        }
        Assert.Fail("No " + mode + " owner appeared.");
    }

    [Given("an isolated service with a connected Claude session")]
    public async Task Setup() {
        Directory.CreateDirectory(directory);
        await StartService();
        await Call("join", new { tool = "codex", sessionId = Sender, topic = "test" });
        await Call("join", new { tool = "claude-code", sessionId = Reader, topic = "test" });
        // Stands in for the session's MCP bridge, which renews its heartbeat every few seconds.
        heartbeat = new CancellationTokenSource();
        heartbeats = Task.Run(async () => {
            while (!heartbeat.IsCancellationRequested) {
                try { _ = await LocalHttpClient.ExecuteAsync(port, "bridge_heartbeat", new { id = bridgeId, mode = "mcp", sessionId = Reader }, heartbeat.Token); }
                catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException) { }
                try { await Task.Delay(1000, heartbeat.Token); }
                catch (OperationCanceledException) { }
            }
        });
        await Task.Delay(1500);
    }

    [Then("a waiter for an unknown session exits 0 at once without output")]
    public async Task Unknown() {
        var timer = Stopwatch.StartNew();
        var waiter = await StartWaiter("unknown-session");
        Assert.Equal(0, await Exit(waiter));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(8), timer.Elapsed.ToString());
        Assert.Equal("", await waiter.Output);
        Assert.Equal("", await waiter.Errors);
    }

    [When("a waiter is running and two messages arrive")]
    public async Task WaitThenPublish() {
        var waiter = await StartWaiter();
        await WaitForOwner("claude-hook");
        Assert.False(waiter.Process.HasExited);
        await Publish();
        await Publish();
        Assert.Equal(2, await Exit(waiter));
        var errors = (await waiter.Errors).TrimEnd('\r', '\n');
        Assert.StartsWith("Pigeon wake generation ", errors);
        Assert.DoesNotContain('\n', errors);
        Assert.DoesNotContain("synthetic", errors);
        Assert.DoesNotContain("untrusted-extra-data", errors);
        Assert.Equal("", await waiter.Output);
    }

    [Then("it writes only the fixed wake instruction to stderr, exits 2 and releases its ownership")]
    public async Task Woke() {
        var status = await WakeStatus();
        Assert.Equal(2, status.GetProperty("Queued").GetInt32());
        Assert.Equal(0, status.GetProperty("Leased").GetInt32());
        Assert.Equal(1, status.GetProperty("Attempts").GetInt32());
        Assert.Equal("transport-written", status.GetProperty("LastOutcome").GetString());
        Assert.Equal("claude-hook", status.GetProperty("History")[0].GetProperty("Mode").GetString());
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("woke", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
    }

    [Then("a second waiter replaces the first, which leaves quietly")]
    public async Task Supersede() {
        var first = await StartWaiter();
        await WaitForOwner("claude-hook");
        var second = await StartWaiter();
        Assert.Equal(0, await Exit(first));
        Assert.Equal("", await first.Errors);
        Assert.False(second.Process.HasExited);
        await Publish();
        Assert.Equal(2, await Exit(second));
        var wake = await second.Errors;
        Assert.StartsWith("Pigeon wake generation ", wake);
        // Drain as the agent would; the next message then starts a new claimable wake.
        var generation = wake.Split(' ')[3].TrimEnd('.');
        var batch = await Call("wake_receive", new { sessionId = Reader, generation });
        Assert.True(batch.GetProperty("Accepted").GetBoolean());
        Assert.Single(batch.GetProperty("Messages").EnumerateArray());
        Assert.True((await Call("wake_complete", new { sessionId = Reader, generation })).GetBoolean());
        Assert.Equal(0, (await WakeStatus()).GetProperty("Queued").GetInt32());
        var third = await StartWaiter();
        await WaitForOwner("claude-hook");
        await Publish("second message");
        Assert.Equal(2, await Exit(third));
    }

    [Then("a waiter with a short timeout exits 0 without output and status reports that it expired")]
    public async Task Expired() {
        var waiter = await StartWaiter("reader-session", "--timeout", "10");
        Assert.Equal(0, await Exit(waiter, 25));
        Assert.Equal("", await waiter.Errors);
        Assert.Equal("", await waiter.Output);
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("expired", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
        var status = Pigeon("status");
        var text = await status.StandardOutput.ReadToEndAsync();
        await status.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("notification: none (last claude-hook expired at ", text);
    }

    [Then("a short renewable waiter requests one renewal and its replacement wakes for a message")]
    public async Task Renewal() {
        var waiter = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        Assert.Equal(2, await Exit(waiter, 25));
        Assert.Equal("", await waiter.Output);
        var text = await waiter.Errors;
        Assert.StartsWith("Pigeon notification waiter needs renewal.", text);
        Assert.DoesNotContain("Pigeon wake generation", text);
        Assert.Equal("renewed", (await Notifier()).GetProperty("lastRelease").GetProperty("reason").GetString());
        Assert.Equal(0, (await WakeStatus()).GetProperty("Attempts").GetInt32());
        var next = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        await Publish("after renewal");
        Assert.Equal(2, await Exit(next, 20));
        Assert.StartsWith("Pigeon wake generation ", await next.Errors);
    }

    [Then("a short renewable waiter whose service stops before the deadline requests one renewal, and its replacement waits for the service")]
    public async Task RenewalDuringOutage() {
        var waiter = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        await StopService();
        Assert.Equal(2, await Exit(waiter, 25));
        Assert.StartsWith("Pigeon notification waiter needs renewal.", await waiter.Errors);
        // The replacement never reaches the service, so it cannot own the session or renew again.
        var replacement = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        Assert.Equal(0, await Exit(replacement, 25));
        Assert.Equal("", await replacement.Errors);
        await StartService();
        await Task.Delay(1500); // Let the stand-in MCP bridge heartbeat reach the restarted service.
        var next = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        await Publish("after outage");
        Assert.Equal(2, await Exit(next, 20));
        Assert.StartsWith("Pigeon wake generation ", await next.Errors);
    }

    [Then("status names the service and CLI versions and flags that no notifier was seen for the session")]
    public async Task StatusDiagnostics() {
        var status = Pigeon("status");
        var output = await status.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await status.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, status.ExitCode);
        // This service runs inside the test host, so its version differs from the CLI's and status says so.
        Assert.Contains("Pigeon service: running, version ", output);
        Assert.Contains("this CLI is ", output);
        Assert.Contains("notification: none (no notifier seen since the service started", output);
    }

    [Then("a session in no topic and with nothing waiting expires without a renewal")]
    public async Task UnsubscribedRenewal() {
        var waiter = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        await Call("leave", new { sessionId = Reader, topic = "test" });
        Assert.Equal(0, await Exit(waiter, 25));
        Assert.Equal("", await waiter.Errors);
        Assert.Equal("expired", (await Notifier()).GetProperty("lastRelease").GetProperty("reason").GetString());
    }

    [Then("the bridge lookup finds only the session's own ordinary MCP bridge")]
    public async Task SessionBridgeLookup() {
        Assert.True((await Call("claude_session_bridges", new { sessionId = Reader })).GetBoolean());
        await Call("join", new { tool = "claude-code", sessionId = "different-session", topic = "other" });
        await Call("bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "claude-hook", sessionId = "different-session" });
        Assert.False((await Call("claude_session_bridges", new { sessionId = "different-session" })).GetBoolean());
        // A Codex session's bridge never counts as a Claude session's.
        await Call("bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "mcp", sessionId = Sender });
        Assert.False((await Call("claude_session_bridges", new { sessionId = Sender })).GetBoolean());
        Assert.False((await Call("claude_session_bridges", new { sessionId = "unknown-session" })).GetBoolean());
    }

    [Then("a notifier that stops renewing is reported lost after its ownership lapses")]
    public async Task LostOwner() {
        var holder = Guid.NewGuid().ToString("N");
        Assert.True((await Call("wake_owner", new { sessionId = Reader, holderId = holder, mode = "claude-hook" })).GetProperty("owner").GetBoolean());
        await Task.Delay(TimeSpan.FromSeconds(16));
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("lost", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
    }

    [Then("malformed hook input exits 0 and invalid waiter arguments exit 1, without a wake")]
    public async Task Malformed() {
        foreach (var input in new[] { "not json", "{}", JsonSerializer.Serialize(new { session_id = 5 }), new string('x', 9000) }) {
            var waiter = await StartWaiterWithInput(input);
            Assert.Equal(0, await Exit(waiter));
            Assert.Equal("", await waiter.Errors);
        }
        foreach (var args in new[] { new[] { "--timeout", "5" }, new[] { "--timeout", "soon" }, new[] { "--other", "10" } }) {
            var waiter = await StartWaiter("reader-session", args);
            Assert.Equal(1, await Exit(waiter));
        }
        var codex = Pigeon("hook", "wait", "codex");
        codex.StandardInput.Close();
        await codex.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, codex.ExitCode);
        Assert.Equal(0, (await WakeStatus()).GetProperty("Attempts").GetInt32());
    }

    [Then("a waiter started before a service restart wakes for a message published after it")]
    public async Task Restart() {
        var waiter = await StartWaiter();
        await WaitForOwner("claude-hook");
        await StopService();
        await Task.Delay(1500);
        Assert.False(waiter.Process.HasExited);
        await StartService();
        await Publish("after restart");
        Assert.Equal(2, await Exit(waiter, 20));
        var errors = await waiter.Errors;
        Assert.StartsWith("Pigeon wake generation ", errors);
        Assert.DoesNotContain("after restart", errors);
    }

    [Then("only the owning waiter can claim, and only its holder can release ownership")]
    public async Task Ownership() {
        await Publish();
        string first = Guid.NewGuid().ToString("N"), second = Guid.NewGuid().ToString("N");
        Assert.True((await Call("wake_owner", new { sessionId = Reader, holderId = first, mode = "claude-hook" })).GetProperty("owner").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await Call("wake_claim", new { sessionId = Reader, mode = "claude-hook" })).ValueKind);
        // A newer holder replaces the older one, which cannot take it back.
        Assert.True((await Call("wake_owner", new { sessionId = Reader, holderId = second, mode = "claude-hook" })).GetProperty("owner").GetBoolean());
        Assert.False((await Call("wake_owner", new { sessionId = Reader, holderId = first, mode = "claude-hook" })).GetProperty("owner").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await Call("wake_claim", new { sessionId = Reader, mode = "claude-hook", holderId = first })).ValueKind);
        Assert.Equal(0, (await WakeStatus()).GetProperty("Attempts").GetInt32());
        Assert.Equal(JsonValueKind.Object, (await Call("wake_claim", new { sessionId = Reader, mode = "claude-hook", holderId = second })).ValueKind);
        Assert.False((await Call("wake_owner_release", new { sessionId = Reader, holderId = first, reason = "stopped" })).GetProperty("released").GetBoolean());
        Assert.True((await Call("wake_owner_release", new { sessionId = Reader, holderId = second, reason = "stopped" })).GetProperty("released").GetBoolean());
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("stopped", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
        var invalid = await LocalHttpClient.ExecuteAsync(port, "wake_owner", new { sessionId = Reader, holderId = "not-a-guid", mode = "claude-hook" });
        Assert.False(invalid.GetProperty("Ok").GetBoolean());
        var unknown = await LocalHttpClient.ExecuteAsync(port, "wake_owner", new { sessionId = "nobody", holderId = Guid.NewGuid().ToString("N"), mode = "claude-hook" });
        Assert.False(unknown.GetProperty("Ok").GetBoolean());
    }

    [Then("a waiter started during a held database lock keeps waiting and wakes after the lock is released")]
    public async Task HeldLock() {
        Waiter waiter;
        using (var other = new Sqlite(Path.Combine(directory, "messages.db"))) {
            _ = other.Exec("BEGIN IMMEDIATE");
            waiter = await StartWaiter();
            // Its first restore waits out the lock and is reported as storage unavailable, not as an unknown session.
            await Task.Delay(4000);
            Assert.False(waiter.Process.HasExited);
            _ = other.Exec("ROLLBACK");
        }
        await WaitForOwner("claude-hook");
        await Publish();
        Assert.Equal(2, await Exit(waiter));
        Assert.StartsWith("Pigeon wake generation ", await waiter.Errors);
    }

    [Then("a waiter under two Git Bash shells exits as orphaned when only the outer shell is killed, and a later message is not claimed")]
    public async Task OuterShellKilled() {
        var bash = Environment.GetEnvironmentVariable("PIGEON_TEST_BASH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        Assert.True(File.Exists(bash), "Git Bash is required for this scenario; set PIGEON_TEST_BASH to its executable.");
        // Git Bash's launcher starts a second bash, which runs the waiter: test -> bash -> bash -> pigeon.
        var pigeon = Path.Combine(AppContext.BaseDirectory, "pigeon.exe").Replace('\\', '/');
        var start = new ProcessStartInfo(bash) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"\"{pigeon}\" hook wait claude-code --timeout 60");
        start.Environment["PIGEON_PORT"] = port;
        var outer = Process.Start(start)!;
        processes.Add(outer);
        _ = outer.StandardOutput.ReadToEndAsync();
        _ = outer.StandardError.ReadToEndAsync();
        await outer.StandardInput.WriteAsync(JsonSerializer.Serialize(new { session_id = "reader-session", hook_event_name = "Stop" }));
        outer.StandardInput.Close();
        await WaitForOwner("claude-hook");
        outer.Kill(entireProcessTree: false);
        for (var i = 0; i < 30 && (await Notifier()).GetProperty("active").ValueKind != JsonValueKind.Null; i++) await Task.Delay(300);
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("orphaned", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
        await Publish();
        await Task.Delay(2500);
        Assert.Equal(0, (await WakeStatus()).GetProperty("Attempts").GetInt32());
    }

    [AfterScenario]
    public async Task Cleanup() {
        foreach (var process in processes) {
            if (!process.HasExited) process.Kill();
            await process.WaitForExitAsync();
            process.Dispose();
        }
        if (heartbeat is not null) { heartbeat.Cancel(); await heartbeats; heartbeat.Dispose(); }
        if (stop is not null) { stop.Cancel(); try { await server; } catch (OperationCanceledException) { } stop.Dispose(); }
        broker?.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
