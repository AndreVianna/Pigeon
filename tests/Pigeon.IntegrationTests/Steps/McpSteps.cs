using System.Text.Json;
using Pigeon.Service;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "mcp")]
public sealed class McpSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-mcp-" + Guid.NewGuid().ToString("N"));
    private readonly string port = TestPorts.FreePort();
    private Broker? broker;
    private CancellationTokenSource? stop;
    private Task? server;
    private McpSession codex = null!, claude = null!;
    private int id;
    private const string CodexSession = "8d77a0ec-d9c8-46b4-9948-7831ad4609ef", ClaudeSession = "c1122145-145c-4af1-b02b-c2731e423fed";

    private async Task<McpSession> Connect(string? runtime = null, string? autoSession = null) {
        var session = new McpSession(port, runtime, autoSession);
        Assert.Contains("protocolVersion", await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}"));
        Assert.Null(await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
        return session;
    }

    private async Task<(bool Error, string Text)> Call(McpSession session, string tool, object args) {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = ++id, method = "tools/call", @params = new { name = tool, arguments = args } });
        using var response = JsonDocument.Parse((await session.HandleAsync(request))!);
        var result = response.RootElement.GetProperty("result");
        return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }

    private async Task<JsonElement> Ok(McpSession session, string tool, object args) {
        var (error, text) = await Call(session, tool, args);
        Assert.False(error, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Given("a running service with two agent connections")]
    public async Task Running() {
        Directory.CreateDirectory(directory);
        broker = new Broker(Path.Combine(directory, "messages.db"));
        stop = new CancellationTokenSource();
        var host = new LocalHttpServer(port, broker);
        server = host.RunAsync(stop.Token);
        await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        codex = await Connect("codex");
        claude = await Connect("claude-code");
    }

    [Given("a connection to a service that is not running")]
    public async Task NotRunning() => codex = await Connect("codex");

    [Then("the tool list names exactly the seven Pigeon tools")]
    public async Task ToolList() {
        using var response = JsonDocument.Parse((await codex.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/list\"}"))!);
        var names = response.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!).Order().ToArray();
        Assert.Equal(["pigeon_join", "pigeon_leave", "pigeon_receive", "pigeon_send", "pigeon_session_restore", "pigeon_topics", "pigeon_wake_complete"], names);
        var removed = await codex.HandleAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 10, method = "tools/call", @params = new { name = "pigeon_register", arguments = new { agent = "old" } } }));
        Assert.Contains("\"error\"", removed);
    }

    [Then("tools other than join ask for a join first, the first join binds the session, and joining as another session is refused")]
    public async Task Binding() {
        var (error, text) = await Call(codex, "pigeon_send", new { topic = "review", message = "too early" });
        Assert.True(error);
        Assert.Equal("Join a topic first with pigeon_join.", text);
        var joined = await Ok(codex, "pigeon_join", new { topic = "review", sessionId = CodexSession });
        Assert.Equal("codex", joined.GetProperty("Members")[0].GetProperty("Tool").GetString());
        _ = await Ok(codex, "pigeon_join", new { topic = "second", sessionId = CodexSession });
        (error, text) = await Call(codex, "pigeon_join", new { topic = "review", sessionId = ClaudeSession });
        Assert.True(error);
        Assert.Contains("bound to another session", text);
        var topics = await Ok(codex, "pigeon_topics", new { });
        Assert.Equal(["review", "second"], topics.EnumerateArray().Select(t => t.GetProperty("Topic").GetString()));
        // Without a configured tool, join needs one.
        var bare = await Connect();
        (error, text) = await Call(bare, "pigeon_join", new { topic = "third", sessionId = "bare-session" });
        Assert.True(error);
        Assert.Contains("Pass tool", text);
        _ = await Ok(bare, "pigeon_join", new { topic = "third", sessionId = "bare-session", tool = "cursor" });
    }

    [Then("a send says nobody else is in the topic, then that the recipient is unavailable, then that it was sent once the recipient is connected")]
    public async Task Delivery() {
        _ = await Ok(codex, "pigeon_join", new { topic = "review", sessionId = CodexSession });
        Assert.Equal("no-recipient", (await Ok(codex, "pigeon_send", new { topic = "review", message = "anyone?" })).GetProperty("delivery").GetString());
        _ = await Ok(claude, "pigeon_join", new { topic = "review", sessionId = ClaudeSession });
        var offline = await Ok(codex, "pigeon_send", new { topic = "review", message = "are you there?" });
        Assert.Equal("recipient-offline", offline.GetProperty("delivery").GetString());
        Assert.StartsWith("The recipient is unavailable right now", offline.GetProperty("text").GetString());
        _ = await claude.BridgeAsync("bridge_heartbeat", new { id = Guid.NewGuid().ToString("N"), mode = "mcp", sessionId = ClaudeSession }, CancellationToken.None);
        var sent = await Ok(codex, "pigeon_send", new { topic = "review", message = "hello" });
        Assert.Equal(("sent", "Sent."), (sent.GetProperty("delivery").GetString(), sent.GetProperty("text").GetString()));
    }

    [Then("the Claude connection receives the message under its wake and completing the wake leaves nothing waiting")]
    public async Task WakeThroughTools() {
        _ = await Ok(codex, "pigeon_join", new { topic = "review", sessionId = CodexSession });
        _ = await Ok(claude, "pigeon_join", new { topic = "review", sessionId = ClaudeSession });
        _ = await Ok(codex, "pigeon_send", new { topic = "review", message = "please review" });
        var holder = Guid.NewGuid().ToString("N");
        _ = await claude.BridgeAsync("wake_owner", new { sessionId = ClaudeSession, holderId = holder, mode = "claude-hook" }, CancellationToken.None);
        var claim = await claude.BridgeAsync("wake_claim", new { sessionId = ClaudeSession, mode = "claude-hook", holderId = holder }, CancellationToken.None);
        var generation = claim.GetProperty("Value").GetProperty("Generation").GetString();
        Assert.False((await Ok(claude, "pigeon_receive", new { generation = Guid.NewGuid().ToString("N") })).GetProperty("Accepted").GetBoolean());
        var batch = await Ok(claude, "pigeon_receive", new { generation });
        var message = Assert.Single(batch.GetProperty("Messages").EnumerateArray());
        Assert.Equal(("review", "codex", CodexSession, "please review"), (message.GetProperty("Topic").GetString(), message.GetProperty("SenderTool").GetString(), message.GetProperty("SenderSessionId").GetString(), message.GetProperty("Body").GetString()));
        Assert.True((await Ok(claude, "pigeon_wake_complete", new { generation })).GetBoolean());
        var topics = await Ok(claude, "pigeon_topics", new { });
        Assert.Equal(0, topics[0].GetProperty("Waiting").GetInt32());
    }

    [Then("a new connection configured for claude-code with a joined session ID is bound without calling join")]
    public async Task AutoRestore() {
        _ = await Ok(claude, "pigeon_join", new { topic = "review", sessionId = ClaudeSession });
        var restarted = await Connect("claude-code", ClaudeSession);
        Assert.Equal("pending", restarted.RestoreState);
        await restarted.TryRestoreAsync(CancellationToken.None);
        Assert.Equal(("bound", ClaudeSession), (restarted.RestoreState, restarted.SessionId));
        Assert.Equal("review", (await Ok(restarted, "pigeon_topics", new { }))[0].GetProperty("Topic").GetString());
        // An unknown session is not created.
        var unknown = await Connect("claude-code", "never-joined");
        await unknown.TryRestoreAsync(CancellationToken.None);
        Assert.Equal("not-configured", unknown.RestoreState);
        Assert.Null(unknown.SessionId);
    }

    [Then("the HTTP endpoint rejects browser origins and non-loopback hostnames")]
    public async Task RejectBrowserRequests() {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        foreach (var badHost in new[] { false, true }) {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/broker");
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("X-Pigeon-Client", "1");
            if (badHost) request.Headers.Host = "attacker.example";
            else request.Headers.Add("Origin", "https://attacker.example");
            using var response = await client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Then("a tool call reports that the Pigeon service is unavailable")]
    public async Task Unavailable() {
        var (error, text) = await Call(codex, "pigeon_join", new { topic = "review", sessionId = CodexSession });
        Assert.True(error);
        Assert.Equal("The Pigeon service is unavailable.", text);
    }

    [AfterScenario]
    public async Task Cleanup() {
        if (stop is not null) {
            stop.Cancel();
            try { await server!; } catch (OperationCanceledException) { }
            stop.Dispose();
        }
        broker?.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
