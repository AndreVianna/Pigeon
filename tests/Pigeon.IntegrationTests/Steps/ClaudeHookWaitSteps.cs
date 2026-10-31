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
    private readonly Principal sender = new(Mailbox.TopicNamespace, "sender");
    private readonly Principal reader = new(Mailbox.TopicNamespace, "reader");
    private readonly string bridgeId = Guid.NewGuid().ToString("N");
    private readonly List<Process> processes = [];
    private Broker broker = null!;
    private CancellationTokenSource stop = null!;
    private Task server = null!;
    private CancellationTokenSource heartbeat = null!;
    private Task heartbeats = null!;
    private int key;

    private async Task<JsonElement> Call(Principal principal, string operation, object args) {
        var result = await LocalHttpClient.ExecuteAsync(port, principal, operation, args);
        Assert.True(result.GetProperty("Ok").GetBoolean(), result.ToString());
        return result.GetProperty("Value");
    }

    private Task Publish(string body = "synthetic") =>
        Call(sender, "publish", new { topic = "test", body, correlationId = "test", idempotencyKey = "key-" + ++key, expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() });

    private async Task StartService() {
        broker = new Broker(Path.Combine(directory, "messages.db"), topicMode: true);
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
        var bridges = await Call(sender, "bridges", new { });
        return bridges.EnumerateArray().First(b => b.GetProperty("agent").GetString() == "reader" && b.GetProperty("mode").GetString() == "mcp").GetProperty("notifier");
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
        await Call(sender, "register", new { runtime = "codex", sessionId = "sender-session" });
        await Call(reader, "register", new { runtime = "claude-code", sessionId = "reader-session" });
        await Call(sender, "topic_create", new { topic = "test" });
        await Call(reader, "subscribe", new { topic = "test" });
        // Stands in for the session's MCP bridge, which renews its heartbeat every few seconds.
        heartbeat = new CancellationTokenSource();
        heartbeats = Task.Run(async () => {
            while (!heartbeat.IsCancellationRequested) {
                try { _ = await LocalHttpClient.ExecuteAsync(port, reader, "bridge_heartbeat", new { id = bridgeId, mode = "mcp" }, heartbeat.Token); }
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
        var status = await Call(reader, "wake_status", new { });
        Assert.Equal(2, status.GetProperty("Queued").GetInt32());
        Assert.Equal(0, status.GetProperty("Leased").GetInt32());
        Assert.Equal(1, status.GetProperty("Attempts").GetInt32());
        Assert.Equal("transport-written", status.GetProperty("LastOutcome").GetString());
        Assert.Equal("claude-hook", status.GetProperty("History")[0].GetProperty("Mode").GetString());
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("woke", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
    }

    [Then("a second waiter replaces the first, and a waiter waits behind a live monitor owner until it stops")]
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
        // Drain as the agent would, so the next message starts a new claimable wake.
        var generation = wake.Split(' ')[3].TrimEnd('.');
        Assert.True((await Call(reader, "wake_confirm", new { generation })).GetProperty("Accepted").GetBoolean());
        var message = await Call(reader, "receive", new { });
        await Call(reader, "acknowledge", new { receipt = message.GetProperty("Receipt").GetString() });
        await Call(reader, "wake_complete", new { generation });

        var monitor = Guid.NewGuid().ToString("N");
        Assert.True((await Call(reader, "wake_owner", new { holderId = monitor, mode = "claude-monitor" })).GetProperty("owner").GetBoolean());
        var waiting = await StartWaiter();
        await Publish();
        await Task.Delay(3000);
        // The waiter neither claims nor leaves while the monitor owns the session.
        Assert.False(waiting.Process.HasExited);
        Assert.Equal("claude-monitor", (await Notifier()).GetProperty("active").GetString());
        var pending = await Call(reader, "wake_status", new { });
        Assert.Equal("ready", pending.GetProperty("State").GetString());
        Assert.Equal(0, pending.GetProperty("Attempts").GetInt32());
        // A monitor stopped without warning releases nothing; here it releases explicitly.
        Assert.True((await Call(reader, "wake_owner_release", new { holderId = monitor, reason = "stopped" })).GetProperty("released").GetBoolean());
        Assert.Equal(2, await Exit(waiting));
        Assert.StartsWith("Pigeon wake generation ", await waiting.Errors);
        var claimed = await Call(reader, "wake_status", new { });
        Assert.Equal(1, claimed.GetProperty("Attempts").GetInt32());
        Assert.Equal("claude-hook", claimed.GetProperty("History")[0].GetProperty("Mode").GetString());
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
        var status = Pigeon("status", port);
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
        Assert.Equal(0, (await Call(reader, "wake_status", new { })).GetProperty("Attempts").GetInt32());
        var next = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        await Publish("after renewal");
        Assert.Equal(2, await Exit(next, 20));
        Assert.StartsWith("Pigeon wake generation ", await next.Errors);
    }

    [Then("a short renewable waiter behind a monitor expires without a renewal")]
    public async Task BlockedRenewal() {
        var monitor = Guid.NewGuid().ToString("N");
        Assert.True((await Call(reader, "wake_owner", new { holderId = monitor, mode = "claude-monitor" })).GetProperty("owner").GetBoolean());
        var waiter = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        Assert.Equal(0, await Exit(waiter, 25));
        Assert.Equal("", await waiter.Errors);
        Assert.Equal("claude-monitor", (await Notifier()).GetProperty("active").GetString());
        Assert.Equal(0, (await Call(reader, "wake_status", new { })).GetProperty("Attempts").GetInt32());
    }

    [Then("an unsubscribed idle session expires without a renewal")]
    public async Task UnsubscribedRenewal() {
        var waiter = await StartWaiter("reader-session", "--timeout", "10", "--renew");
        await WaitForOwner("claude-hook");
        await Call(reader, "subscribe", new { topic = "test", subscribed = false });
        Assert.Equal(0, await Exit(waiter, 25));
        Assert.Equal("", await waiter.Errors);
        Assert.Equal("expired", (await Notifier()).GetProperty("lastRelease").GetProperty("reason").GetString());
    }

    [Then("the lightweight bridge lookup returns only distinct matching ordinary MCP identities")]
    public async Task SessionBridgeLookup() {
        await Call(reader, "bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "mcp" });
        await Call(reader, "bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "claude-hook" });
        var another = new Principal(Mailbox.TopicNamespace, "another-reader");
        await Call(another, "register", new { runtime = "claude-code", sessionId = "different-session" });
        await Call(another, "bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "mcp" });
        var found = await Call(sender, "claude_session_bridges", new { sessionId = "reader-session" });
        Assert.Equal(new[] { "reader" }, found.EnumerateArray().Select(item => item.GetString()).ToArray());
        var missing = await Call(sender, "claude_session_bridges", new { sessionId = "unknown-session" });
        Assert.Empty(missing.EnumerateArray());
    }

    [Then("a notifier that stops renewing is reported lost after its ownership lapses")]
    public async Task LostOwner() {
        var holder = Guid.NewGuid().ToString("N");
        Assert.True((await Call(reader, "wake_owner", new { holderId = holder, mode = "claude-hook" })).GetProperty("owner").GetBoolean());
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
        Assert.Equal(0, (await Call(reader, "wake_status", new { })).GetProperty("Attempts").GetInt32());
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

    [Then("only the owning notifier can claim, and only its holder can release ownership")]
    public async Task Ownership() {
        await Publish();
        string first = Guid.NewGuid().ToString("N"), channel = Guid.NewGuid().ToString("N"), second = Guid.NewGuid().ToString("N");
        Assert.True((await Call(reader, "wake_owner", new { holderId = first, mode = "claude-monitor" })).GetProperty("owner").GetBoolean());
        var refused = await Call(reader, "wake_owner", new { holderId = channel, mode = "claude-channel" });
        Assert.False(refused.GetProperty("owner").GetBoolean());
        Assert.Equal("claude-monitor", refused.GetProperty("active").GetString());
        Assert.Equal(JsonValueKind.Null, (await Call(reader, "wake_claim", new { mode = "claude-channel", holderId = channel })).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await Call(reader, "wake_claim", new { mode = "claude-monitor", sessionId = "reader-session" })).ValueKind);
        // A newer holder of the same mode replaces the older one, which cannot take it back.
        Assert.True((await Call(reader, "wake_owner", new { holderId = second, mode = "claude-monitor" })).GetProperty("owner").GetBoolean());
        Assert.False((await Call(reader, "wake_owner", new { holderId = first, mode = "claude-monitor" })).GetProperty("owner").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await Call(reader, "wake_claim", new { mode = "claude-monitor", sessionId = "reader-session", holderId = first })).ValueKind);
        Assert.Equal(0, (await Call(reader, "wake_status", new { })).GetProperty("Attempts").GetInt32());
        Assert.Equal(JsonValueKind.Object, (await Call(reader, "wake_claim", new { mode = "claude-monitor", sessionId = "reader-session", holderId = second })).ValueKind);
        Assert.False((await Call(reader, "wake_owner_release", new { holderId = first, reason = "stopped" })).GetProperty("released").GetBoolean());
        Assert.True((await Call(reader, "wake_owner_release", new { holderId = second, reason = "stopped" })).GetProperty("released").GetBoolean());
        var notifier = await Notifier();
        Assert.Equal(JsonValueKind.Null, notifier.GetProperty("active").ValueKind);
        Assert.Equal("stopped", notifier.GetProperty("lastRelease").GetProperty("reason").GetString());
        // With no live owner, the replaced holder may take over again.
        Assert.True((await Call(reader, "wake_owner", new { holderId = first, mode = "claude-monitor" })).GetProperty("owner").GetBoolean());
        var invalid = await LocalHttpClient.ExecuteAsync(port, reader, "wake_owner", new { holderId = "not-a-guid", mode = "claude-monitor" });
        Assert.False(invalid.GetProperty("Ok").GetBoolean());
        var unregistered = await LocalHttpClient.ExecuteAsync(port, new Principal(Mailbox.TopicNamespace, "nobody"), "wake_owner", new { holderId = Guid.NewGuid().ToString("N"), mode = "claude-hook" });
        Assert.False(unregistered.GetProperty("Ok").GetBoolean());
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
