using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "session_routes")]
public sealed class SessionRoutingSteps {
    private readonly string dir = Path.Combine(Path.GetTempPath(), "pigeon-session-route-" + Guid.NewGuid().ToString("N"));
    private readonly string port = TestPorts.FreePort();
    private Broker broker = null!;
    private CancellationTokenSource stop = null!;
    private Task server = null!;
    private McpSession a = null!, b = null!, codex = null!;
    private System.Diagnostics.Process? bridgeProcess;
    private string Database => Path.Combine(dir, "data.db");

    private async Task<McpSession> NewSession() {
        var s = new McpSession(port, new Principal(Mailbox.TopicNamespace, "unregistered-" + Guid.NewGuid().ToString("N")), requireRegistrationName: true);
        await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
        await s.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        return s;
    }
    private static async Task<(bool Error, JsonElement Value)> Call(McpSession session, string name, object args) {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name, arguments = args } });
        using var reply = JsonDocument.Parse((await session.HandleAsync(request))!);
        var result = reply.RootElement.GetProperty("result");
        var text = result.GetProperty("content")[0].GetProperty("text").GetString()!;
        if (result.GetProperty("isError").GetBoolean()) return (true, JsonSerializer.SerializeToElement(text));
        return (false, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Given("an isolated topic service with two Claude sessions and a Codex session")]
    public async Task Setup() {
        Directory.CreateDirectory(dir);
        broker = new Broker(Database, topicMode: true);
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        a = await NewSession(); b = await NewSession(); codex = await NewSession();
    }

    [Then("direct messages and restored bindings use the tool and session pair")]
    public async Task RouteSessions() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "one" })).Error);
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "two" })).Error);
        var first = await Call(a, "pigeon_session_join", new { tool = "Claude", sessionId = "session-a", topic = "one" });
        var second = await Call(b, "pigeon_session_join", new { tool = "claude-code", sessionId = "session-b", topic = "two" });
        var sender = await Call(codex, "pigeon_session_join", new { tool = "codex", sessionId = "session-a", topic = "one" });
        Assert.False(first.Error); Assert.False(second.Error); Assert.False(sender.Error);
        Assert.NotEqual(a.CurrentPrincipal.Agent, b.CurrentPrincipal.Agent);
        Assert.NotEqual(a.CurrentPrincipal.Agent, codex.CurrentPrincipal.Agent);
        Assert.False((await Call(codex, "pigeon_send_session", new { recipientTool = "claude-code", recipientSessionId = "session-a", topic = "one", body = "for a" })).Error);
        Assert.True((await Call(codex, "pigeon_send_session", new { recipientTool = "claude-code", recipientSessionId = "session-b", topic = "two", body = "wrong topic" })).Error);
        // Restart the service before binding a new bridge and receiving the stored message.
        stop.Cancel(); await server; stop.Dispose(); broker.Dispose();
        broker = new Broker(Database, topicMode: true);
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        var restored = await NewSession();
        var restore = await Call(restored, "pigeon_session_restore", new { tool = "claude-code", sessionId = "session-a" });
        Assert.False(restore.Error);
        Assert.Equal(a.CurrentPrincipal, restored.CurrentPrincipal);
        Assert.Equal("one", restore.Value.GetProperty("Topics")[0].GetString());
        Assert.True((await Call(restored, "pigeon_session_restore", new { tool = "claude-code", sessionId = "session-b" })).Error);
        var message = await Call(restored, "pigeon_receive", new { });
        Assert.False(message.Error);
        Assert.Equal("for a", message.Value.GetProperty("Body").GetString());
        Assert.Equal("codex", message.Value.GetProperty("Sender").GetString());
        Assert.Equal("codex", message.Value.GetProperty("SenderTool").GetString());
        Assert.Equal("session-a", message.Value.GetProperty("SenderSessionId").GetString());
        Assert.Equal(JsonValueKind.Null, (await Call(b, "pigeon_receive", new { })).Value.ValueKind);
        var unknown = await NewSession();
        Assert.Equal(JsonValueKind.Null, (await Call(unknown, "pigeon_session_restore", new { tool = "claude-code", sessionId = "never-joined" })).Value.ValueKind);
        Assert.StartsWith("unregistered-", unknown.CurrentPrincipal.Agent);
    }

    [Then("legacy conflicts are reported without moving queued messages")]
    public async Task LegacyConflict() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "legacy" })).Error);
        Assert.False((await Call(a, "pigeon_topic_join", new { agent = "old-a", runtime = "claude-code", sessionId = "same", topic = "legacy" })).Error);
        // Simulate a duplicate identity from a database created by an older version.
        using (var legacy = new Sqlite(Database))
            _ = legacy.Exec("INSERT INTO agents(project,agent,runtime,session,origin) VALUES(?,?,?,?,?)", Mailbox.TopicNamespace, "old-b", "claude-code", "same", "unknown");
        Assert.False((await Call(b, "pigeon_topic_join", new { agent = "old-b", runtime = "claude-code", sessionId = "same", topic = "legacy" })).Error);
        Assert.False((await Call(codex, "pigeon_topic_join", new { agent = "old-c", runtime = "codex", sessionId = "c", topic = "legacy" })).Error);
        Assert.False((await Call(codex, "pigeon_publish", new { topic = "legacy", body = "legacy queued" })).Error);
        var fresh = await NewSession();
        var restore = await Call(fresh, "pigeon_session_restore", new { tool = "claude-code", sessionId = "same" });
        Assert.True(restore.Error);
        Assert.Contains("Ambiguous legacy", restore.Value.GetString());
        Assert.Equal(1, (await Call(a, "pigeon_inbox_status", new { })).Value.GetProperty("Queued").GetInt32());
        Assert.Equal(1, (await Call(b, "pigeon_inbox_status", new { })).Value.GetProperty("Queued").GetInt32());
        var candidates = await Call(fresh, "pigeon_session_candidates", new { tool = "claude-code", sessionId = "same" });
        Assert.Equal(2, candidates.Value.GetArrayLength());
        Assert.False((await Call(fresh, "pigeon_session_adopt", new { tool = "claude-code", sessionId = "same", legacyAgent = "old-b" })).Error);
        Assert.Equal("old-b", fresh.CurrentPrincipal.Agent);
        Assert.Equal(1, (await Call(fresh, "pigeon_inbox_status", new { })).Value.GetProperty("Queued").GetInt32());
        Assert.Equal(1, (await Call(a, "pigeon_inbox_status", new { })).Value.GetProperty("Queued").GetInt32());
        var other = await NewSession();
        Assert.True((await Call(other, "pigeon_session_adopt", new { tool = "claude-code", sessionId = "same", legacyAgent = "old-a" })).Error);
    }

    [Then("restoring a session never recreates a deleted topic")]
    public async Task DeletedTopic() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "delete-me" })).Error);
        Assert.False((await Call(a, "pigeon_session_join", new { tool = "claude-code", sessionId = "a", topic = "delete-me" })).Error);
        Assert.False((await Call(a, "pigeon_topic_delete", new { topic = "delete-me" })).Error);
        var fresh = await NewSession();
        var restored = await Call(fresh, "pigeon_session_restore", new { tool = "claude-code", sessionId = "a" });
        Assert.False(restored.Error);
        Assert.Empty(restored.Value.GetProperty("Topics").EnumerateArray());
        Assert.Empty((await Call(fresh, "pigeon_topics", new { })).Value.EnumerateArray());
        Assert.Equal(1, (await Call(fresh, "pigeon_inbox_status", new { })).Value.GetProperty("Queued").GetInt32());
    }

    [Then("the startup hook restores only saved associations and the MCP bridge binds on a later handshake")]
    public async Task HookAndBridge() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "saved" })).Error);
        Assert.False((await Call(a, "pigeon_session_join", new { tool = "claude-code", sessionId = "saved-session", topic = "saved" })).Error);
        var expected = a.CurrentPrincipal.Agent;
        async Task<string> RunHook(string session) {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { "hook", "session-start", "claude-code" }) start.ArgumentList.Add(arg);
            start.Environment["PIGEON_PORT"] = port;
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { session_id = session, hook_event_name = "SessionStart", prompt = "untrusted-extra-data" }));
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
            Assert.DoesNotContain("untrusted-extra-data", output);
            return output;
        }
        Assert.Contains("saved association", await RunHook("saved-session"));
        Assert.Equal("", await RunHook("unknown-session"));
        var restored = await NewSession();
        Assert.StartsWith("unregistered-", restored.CurrentPrincipal.Agent);
        var result = await Call(restored, "pigeon_session_restore", new { tool = "claude-code", sessionId = "saved-session" });
        Assert.False(result.Error);
        Assert.Equal(expected, restored.CurrentPrincipal.Agent);
        Assert.Equal("saved", result.Value.GetProperty("Topics")[0].GetString());
    }

    [Then("an unambiguous legacy identity keeps its messages and subscriptions when restored")]
    public async Task AdoptLegacy() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "legacy" })).Error);
        Assert.False((await Call(a, "pigeon_topic_join", new { agent = "old-claude", runtime = "claude-code", sessionId = "legacy-session", topic = "legacy" })).Error);
        Assert.False((await Call(codex, "pigeon_topic_join", new { agent = "old-codex", runtime = "codex", sessionId = "c", topic = "legacy" })).Error);
        Assert.False((await Call(codex, "pigeon_publish", new { topic = "legacy", body = "preserved" })).Error);
        var fresh = await NewSession();
        var restored = await Call(fresh, "pigeon_session_restore", new { tool = "claude-code", sessionId = "legacy-session" });
        Assert.False(restored.Error);
        Assert.Equal("old-claude", fresh.CurrentPrincipal.Agent);
        Assert.Equal("legacy", restored.Value.GetProperty("Topics")[0].GetString());
        Assert.Equal("preserved", (await Call(fresh, "pigeon_receive", new { })).Value.GetProperty("Body").GetString());
        Assert.True((await Call(b, "pigeon_topic_join", new { agent = "other-name", runtime = "claude-code", sessionId = "legacy-session", topic = "legacy" })).Error);
    }

    [Then("a bridge retries a failed restore when the service becomes available")]
    public async Task RestoreAfterOutage() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "outage" })).Error);
        Assert.False((await Call(a, "pigeon_session_join", new { tool = "claude-code", sessionId = "outage-session", topic = "outage" })).Error);
        var expected = a.CurrentPrincipal.Agent;
        stop.Cancel(); await server; stop.Dispose(); broker.Dispose();
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("mcp"); start.ArgumentList.Add(port);
        bridgeProcess = System.Diagnostics.Process.Start(start)!;
        async Task<JsonElement> Request(object request) {
            await bridgeProcess.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await bridgeProcess.StandardInput.FlushAsync();
            var response = await bridgeProcess.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            return JsonDocument.Parse(response!).RootElement.Clone();
        }
        _ = await Request(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25" } });
        await bridgeProcess.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        var unavailable = await Request(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = "pigeon_session_restore", arguments = new { tool = "claude-code", sessionId = "outage-session" } } });
        Assert.True(unavailable.GetProperty("result").GetProperty("isError").GetBoolean());
        broker = new Broker(Database, topicMode: true);
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        var rebound = false;
        for (var i = 0; i < 12 && !rebound; i++) {
            var connection = await Request(new { jsonrpc = "2.0", id = 3 + i, method = "tools/call", @params = new { name = "pigeon_connection", arguments = new { } } });
            var text = connection.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            using var value = JsonDocument.Parse(text);
            rebound = value.RootElement.GetProperty("agent").GetString() == expected;
            if (!rebound) await Task.Delay(500);
        }
        Assert.True(rebound, "Bridge did not restore the saved session after service recovery.");
    }

    [Then("a new bridge can recover its saved environment session without a prompt")]
    public async Task AutoRestoreAfterOutage() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "auto" })).Error);
        Assert.False((await Call(a, "pigeon_session_join", new { tool = "claude-code", sessionId = "auto-session", topic = "auto" })).Error);
        var expected = a.CurrentPrincipal.Agent;
        stop.Cancel(); await server; stop.Dispose(); broker.Dispose();
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("mcp"); start.ArgumentList.Add(port);
        start.Environment["PIGEON_RUNTIME"] = "claude-code";
        start.Environment["CLAUDE_CODE_SESSION_ID"] = "auto-session";
        bridgeProcess = System.Diagnostics.Process.Start(start)!;
        await bridgeProcess.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
        await bridgeProcess.StandardInput.FlushAsync();
        Assert.NotNull(await bridgeProcess.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        await bridgeProcess.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        await bridgeProcess.StandardInput.FlushAsync();
        await Task.Delay(500); // First heartbeat sees the service offline.
        broker = new Broker(Database, topicMode: true);
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        var rebound = false;
        for (var i = 0; i < 15 && !rebound; i++) {
            var result = await LocalHttpClient.ExecuteAsync(port, new Principal(Mailbox.TopicNamespace, "operator"), "bridges", new { });
            rebound = result.GetProperty("Value").EnumerateArray().Any(x => x.GetProperty("agent").GetString() == expected && x.GetProperty("restoration").GetString() == "bound");
            if (!rebound) await Task.Delay(500);
        }
        Assert.True(rebound, "Opted-in bridge did not restore on service recovery.");
    }

    [Then("the CLI can join, list and send using tool and session pairs")]
    public async Task SessionCli() {
        async Task<string> Cli(params string[] args) {
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment["PIGEON_PORT"] = port;
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(process.ExitCode == 0, stderr);
            return stdout;
        }
        await Cli("topics", "create", "cli-test");
        await Cli("topics", "join", "cli-test", "claude-code", "recipient");
        await Cli("topics", "join", "cli-test", "codex", "sender");
        using var members = JsonDocument.Parse(await Cli("topics", "sessions", "cli-test"));
        Assert.Equal(2, members.RootElement.GetArrayLength());
        await Cli("messages", "send-session", "codex", "sender", "claude-code", "recipient", "cli message", "--topic", "cli-test");
        using var received = JsonDocument.Parse(await Cli("messages", "get-session", "claude-code", "recipient", "cli-test"));
        Assert.Equal("cli message", received.RootElement[0].GetProperty("Body").GetString());
        Assert.Equal("codex", received.RootElement[0].GetProperty("Sender").GetString());
        await Cli("messages", "ack-session", "claude-code", "recipient", received.RootElement[0].GetProperty("Receipt").GetString()!);
        await Cli("topics", "leave", "cli-test", "claude-code", "recipient");
        using var remaining = JsonDocument.Parse(await Cli("topics", "sessions", "cli-test"));
        Assert.Single(remaining.RootElement.EnumerateArray());
    }

    [Then("concurrent tool and session joins create distinct durable routes")]
    public async Task ConcurrentJoins() {
        Assert.False((await Call(a, "pigeon_topic_create", new { topic = "parallel" })).Error);
        using var first = new Mailbox(Database);
        using var second = new Mailbox(Database);
        first.EnableTopicMode(); second.EnableTopicMode();
        var joins = await Task.WhenAll(
            Task.Run(() => first.JoinSession(Mailbox.TopicNamespace, "claude-code", "a", "parallel")),
            Task.Run(() => second.JoinSession(Mailbox.TopicNamespace, "claude-code", "b", "parallel")));
        Assert.NotEqual(joins[0].Agent, joins[1].Agent);
        Assert.Equal(joins[0].Agent, first.RestoreSession(Mailbox.TopicNamespace, "claude-code", "a")!.Agent);
        Assert.Equal(joins[1].Agent, first.RestoreSession(Mailbox.TopicNamespace, "claude-code", "b")!.Agent);
    }

    [AfterScenario]
    public async Task Cleanup() {
        if (bridgeProcess is not null) { bridgeProcess.StandardInput.Close(); if (!bridgeProcess.WaitForExit(3000)) bridgeProcess.Kill(); await bridgeProcess.WaitForExitAsync(); bridgeProcess.Dispose(); }
        if (stop is not null) { stop.Cancel(); try { await server; } catch (OperationCanceledException) { } stop.Dispose(); }
        broker?.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
