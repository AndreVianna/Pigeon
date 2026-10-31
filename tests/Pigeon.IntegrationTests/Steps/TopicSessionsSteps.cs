using System.Text.Json;
using Pigeon.Core;
using Pigeon.Service;
using Pigeon.Adapters;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "topic_sessions")]
public sealed class TopicSessionsSteps {
    private readonly string dir = Path.Combine(Path.GetTempPath(), "pigeon-topics-" + Guid.NewGuid().ToString("N"));
    private readonly string pipe = TestPorts.FreePort();
    private readonly Dictionary<string, McpSession> sessions = new();
    private Broker? broker;
    private CancellationTokenSource? stop;
    private Task? server;
    private int wakeCalls;
    private string? wakeTarget;
    private string? oldWake;
    private const string CodexSession = "8d77a0ec-d9c8-46b4-9948-7831ad4609ef";

    [Given("four unregistered MCP sessions share one topic-mode service")]
    public async Task Setup() {
        Directory.CreateDirectory(dir);
        broker = new Broker(Path.Combine(dir, "data.db"), topicMode: true);
        stop = new CancellationTokenSource();
        server = new LocalHttpServer(pipe, broker).RunAsync(stop.Token);
        foreach (var name in new[] { "ana", "bob", "sam", "dana" }) {
            var session = new McpSession(pipe, new Principal(Mailbox.TopicNamespace, "unbound-" + name), requireRegistrationName: true);
            _ = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
            _ = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            sessions.Add(name, session);
        }
    }
    private async Task<(bool Error, string Text)> Call(string agent, string name, object args) {
        var input = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name, arguments = args } });
        using var result = JsonDocument.Parse((await sessions[agent].HandleAsync(input))!);
        var value = result.RootElement.GetProperty("result");
        return (value.GetProperty("isError").GetBoolean(), value.GetProperty("content")[0].GetProperty("text").GetString()!);
    }
    [Then("the HTTP endpoint rejects browser origins and non-loopback hostnames")]
    public async Task RejectBrowserRequests() {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        foreach (var badHost in new[] { false, true }) {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{pipe}/broker");
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("X-Pigeon-Client", "1");
            if (badHost) request.Headers.Host = "attacker.example";
            else request.Headers.Add("Origin", "https://attacker.example");
            using var response = await client.SendAsync(request);
            Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
    [When("a subscriber receives a topic post through the mocked Codex queue")]
    public async Task MockCodexQueue() {
        Assert.False((await Call("ana", "pigeon_topic_create", new { topic = "wake-test" })).Error);
        Assert.False((await Call("ana", "pigeon_topic_join", new { topic = "wake-test", agent = "ana", runtime = "claude-code", sessionId = "mock-claude" })).Error);
        Assert.False((await Call("bob", "pigeon_topic_join", new { topic = "wake-test", agent = "bob", runtime = "codex", sessionId = CodexSession })).Error);
        Assert.False((await Call("ana", "pigeon_publish", new { topic = "wake-test", body = "untrusted fixture text" })).Error);
        var notifier = new CodexQueueNotifier(() => sessions["bob"].CurrentPrincipal, () => CodexSession,
            (principal, operation, args, token) => LocalHttpClient.ExecuteAsync(pipe, principal, operation, args, token),
            (session, _, _) => { wakeCalls++; wakeTarget = session; return Task.FromResult(true); });
        for (var i = 0; i < 4; i++) await notifier.TickAsync(CancellationToken.None);
    }
    [Then("the queue request targets only that session and leaves the delivery unacknowledged")]
    public async Task VerifyMockCodexQueue() {
        Assert.Equal(1, wakeCalls);
        Assert.Equal(CodexSession, wakeTarget);
        var (error, text) = await Call("bob", "pigeon_inbox_status", new { });
        Assert.False(error);
        using var inbox = JsonDocument.Parse(text);
        Assert.Equal(1, inbox.RootElement.GetProperty("Queued").GetInt32());
        Assert.Equal(0, inbox.RootElement.GetProperty("Acknowledged").GetInt32());
        var (activityError, activity) = await Call("bob", "pigeon_activity", new { });
        Assert.False(activityError);
        Assert.Contains("wake-transport-accepted", activity);
    }
    [When("a pending wake is retried through the CLI")]
    public async Task RetryWakeFromCli() {
        Assert.False((await Call("ana", "pigeon_topic_create", new { topic = "wake-test" })).Error);
        Assert.False((await Call("ana", "pigeon_topic_join", new { topic = "wake-test", agent = "ana", runtime = "mock", sessionId = "sender" })).Error);
        Assert.False((await Call("bob", "pigeon_topic_join", new { topic = "wake-test", agent = "bob", runtime = "codex", sessionId = CodexSession })).Error);
        Assert.False((await Call("ana", "pigeon_publish", new { topic = "wake-test", body = "pending" })).Error);
        var claim = await LocalHttpClient.ExecuteAsync(pipe, sessions["bob"].CurrentPrincipal, "wake_claim", new { mode = "codex-queue" });
        oldWake = claim.GetProperty("Value").GetProperty("Generation").GetString();
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "pigeon.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "messages", "retry-wake", "bob" }) info.ArgumentList.Add(arg);
        info.Environment["PIGEON_PORT"] = pipe;
        using var process = System.Diagnostics.Process.Start(info)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, stderr);
        Assert.Contains("Wake retry scheduled", stdout);
    }
    [Then("the old generation is stale and the delivery remains queued")]
    public async Task RetryKeepsDelivery() {
        var stale = await Call("bob", "pigeon_wake_confirm", new { generation = oldWake });
        Assert.False(stale.Error);
        using var confirmation = JsonDocument.Parse(stale.Text);
        Assert.False(confirmation.RootElement.GetProperty("Accepted").GetBoolean());
        var inbox = await Call("bob", "pigeon_inbox_status", new { });
        using var status = JsonDocument.Parse(inbox.Text);
        Assert.Equal(1, status.RootElement.GetProperty("Queued").GetInt32());
        var current = await Call("bob", "pigeon_wake_status", new { });
        using var wake = JsonDocument.Parse(current.Text);
        Assert.Equal("ready", wake.RootElement.GetProperty("State").GetString());
    }
    [Then("a mismatched Claude channel cannot claim a wake and the correct channel confirms one burst")]
    public async Task ClaudeChannelCoalesces() {
        Assert.False((await Call("ana", "pigeon_topic_create", new { topic = "channel-test" })).Error);
        Assert.False((await Call("ana", "pigeon_topic_join", new { topic = "channel-test", agent = "ana", runtime = "mock", sessionId = "sender" })).Error);
        Assert.False((await Call("bob", "pigeon_topic_join", new { topic = "channel-test", agent = "bob", runtime = "claude-code", sessionId = "real-session" })).Error);
        foreach (var body in new[] { "one", "two" }) Assert.False((await Call("ana", "pigeon_publish", new { topic = "channel-test", body })).Error);
        var wrong = new ClaudeChannel(pipe, () => sessions["bob"].CurrentPrincipal, () => "other-session");
        Assert.Null(await wrong.NextAsync(CancellationToken.None));
        var channel = new ClaudeChannel(pipe, () => sessions["bob"].CurrentPrincipal, () => "real-session");
        var notification = Assert.IsType<McpOutboundNotification>(await channel.NextAsync(CancellationToken.None));
        Assert.Null(await channel.NextAsync(CancellationToken.None));
        await notification.Written!(CancellationToken.None);
        using var payload = JsonDocument.Parse(notification.Json);
        var generation = payload.RootElement.GetProperty("params").GetProperty("meta").GetProperty("generation").GetString()!;
        var confirmation = await Call("bob", "pigeon_wake_confirm", new { generation });
        using (var result = JsonDocument.Parse(confirmation.Text)) Assert.True(result.RootElement.GetProperty("Accepted").GetBoolean());
        for (var i = 0; i < 2; i++) {
            var received = await Call("bob", "pigeon_receive", new { topic = "channel-test" });
            using var delivery = JsonDocument.Parse(received.Text);
            Assert.False((await Call("bob", "pigeon_acknowledge", new { receipt = delivery.RootElement.GetProperty("Receipt").GetString() })).Error);
        }
        Assert.False((await Call("bob", "pigeon_wake_complete", new { generation })).Error);
        var state = await Call("bob", "pigeon_wake_status", new { });
        using var status = JsonDocument.Parse(state.Text);
        Assert.Equal("completed", status.RootElement.GetProperty("State").GetString());
        Assert.Contains("transport-written", state.Text);
    }
    [When("two sessions join each of two project topics")]
    public async Task Join() {
        foreach (var topic in new[] { "pigeon", "sigma" }) Assert.False((await Call("ana", "pigeon_topic_create", new { topic })).Error);
        foreach (var (agent, topic) in new[] { ("ana", "pigeon"), ("bob", "pigeon"), ("sam", "sigma"), ("dana", "sigma") }) {
            var result = await Call(agent, "pigeon_topic_join", new { topic, agent, runtime = "mock", sessionId = "session-" + agent });
            Assert.False(result.Error);
            Assert.DoesNotContain("__pigeon_topics_v2__", result.Text);
        }
    }
    [When("one session posts to each topic")]
    public async Task Send() {
        Assert.False((await Call("ana", "pigeon_publish", new { topic = "pigeon", body = "from pigeon" })).Error);
        Assert.False((await Call("sam", "pigeon_publish", new { topic = "sigma", body = "from sigma" })).Error);
    }
    [Then("only the other subscriber to each topic receives its post")]
    public async Task Deliveries() {
        foreach (var (agent, expected) in new[] { ("bob", "from pigeon"), ("dana", "from sigma") }) {
            var (error, text) = await Call(agent, "pigeon_receive", new { });
            Assert.False(error);
            using var body = JsonDocument.Parse(text);
            Assert.Equal(expected, body.RootElement.GetProperty("Body").GetString());
        }
        Assert.Equal("null", (await Call("ana", "pigeon_receive", new { })).Text);
        Assert.Equal("null", (await Call("sam", "pigeon_receive", new { })).Text);
    }
    [Then("an unsubscribed session can pull another topic's post")]
    public async Task Pull() {
        var (error, text) = await Call("dana", "pigeon_topic_read", new { topic = "pigeon" });
        Assert.False(error);
        using var posts = JsonDocument.Parse(text);
        Assert.Equal("from pigeon", posts.RootElement[0].GetProperty("Body").GetString());
    }
    [Then("direct messaging between unrelated topics is rejected")]
    public async Task CrossTopic() {
        Assert.True((await Call("ana", "pigeon_send", new { recipient = "dana", body = "not delivered" })).Error);
        Assert.True((await Call("ana", "pigeon_send", new { recipient = "ana", body = "not delivered" })).Error);
        Assert.False((await Call("ana", "pigeon_send", new { recipient = "bob", body = "direct" })).Error);
        var result = await Call("bob", "pigeon_receive", new { topic = "pigeon" });
        Assert.False(result.Error);
        using var message = JsonDocument.Parse(result.Text);
        Assert.Equal("direct", message.RootElement.GetProperty("Body").GetString());
    }
    [Then("direct messaging selects the correct shared topic when sessions share two topics")]
    public async Task MultipleSharedTopics() {
        foreach (var agent in new[] { "ana", "bob" })
            Assert.False((await Call(agent, "pigeon_topic_join", new { topic = "sigma", agent, runtime = "mock", sessionId = "session-" + agent })).Error);
        Assert.True((await Call("ana", "pigeon_send", new { recipient = "bob", body = "ambiguous" })).Error);
        Assert.False((await Call("ana", "pigeon_send", new { recipient = "bob", topic = "sigma", body = "specific" })).Error);
        var result = await Call("bob", "pigeon_receive", new { topic = "sigma" });
        Assert.False(result.Error);
        using var message = JsonDocument.Parse(result.Text);
        Assert.Equal("specific", message.RootElement.GetProperty("Body").GetString());
        Assert.Equal("sigma", message.RootElement.GetProperty("Topic").GetString());
    }
    [AfterScenario]
    public async Task Cleanup() {
        if (stop is not null) { stop.Cancel(); if (server is not null) await server; stop.Dispose(); }
        broker?.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
