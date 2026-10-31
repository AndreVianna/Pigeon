using Pigeon.Service;
using Pigeon.Core;

using Reqnroll;

using System.Text.Json;

using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "messaging")]
public sealed class MessagingSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-bdd-" + Guid.NewGuid().ToString("N"));
    private readonly string pipe = TestPorts.FreePort();
    private long now = 1000;
    private long? retention;
    private int automatedRate = 100;
    private Broker? broker;
    private CancellationTokenSource? stop;
    private Task? server;
    private McpSession codex = null!, claude = null!, secondClaude = null!, other = null!;
    private string messageId = "", secondId = "", receipt = "", previousReceipt = "";
    private string secondDeliveryId = "";
    private bool lastError;
    private int receivedConcurrently;
    private bool secondHostRejected;
    private bool fixtureLimited;
    private System.Diagnostics.Process? fixtureHost, codexProcess, claudeProcess, channelProcess;
    private string processReceipt = "";
    private string wakeGeneration = "";
    private McpSession impersonatingClient = null!;

    [BeforeScenario]
    public async Task Start() {
        _ = Directory.CreateDirectory(directory);
        StartService();
        codex = await Connect("fixture-codex");
        claude = await Connect("fixture-claude");
        secondClaude = await Connect("fixture-claude-two");
        other = await Connect("fixture-other");
        foreach (var (session, expectedProject) in new[] { (codex, "alpha"), (claude, "alpha"), (secondClaude, "alpha"), (other, "beta") }) {
            var (Error, Text) = await Call(session, "pigeon_register", new { runtime = "mock", sessionId = "synthetic-test-session", project = "spoofed" });
            Assert.False(Error);
            using var registered = JsonDocument.Parse(Text);
            Assert.Equal(expectedProject, registered.RootElement.GetProperty("project").GetString());
        }
    }

    [AfterScenario]
    public async Task Cleanup() {
        foreach (var process in new[] { codexProcess, claudeProcess, channelProcess }) {
            if (process is null)
                continue;
            process.StandardInput.Close();
            if (!process.WaitForExit(3000)) {
                process.Kill();
                process.WaitForExit();
            }

            process.Dispose();
        }

        await StopFixtureHost();
        await StopService();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private void StartService() {
        broker = new Broker(Path.Combine(directory, "messages.db"), () => now, retention, maxAutomatedPerMinute: automatedRate);
        stop = new CancellationTokenSource();
        server = new LocalHttpServer(pipe, broker).RunAsync(stop.Token);
    }
    private async Task StopService() {
        if (stop is null)
            return;
        stop.Cancel();
        try {
            if (server is not null)
                await server;
        }
        finally {
            stop.Dispose();
            stop = null;
            broker?.Dispose();
            broker = null;
            server = null;
        }
    }
    private async Task<McpSession> Connect(string token) {
        var principal = token switch {
            "fixture-codex" => new Principal("alpha", "codex"),
            "fixture-claude" => new Principal("alpha", "claude"),
            "fixture-claude-two" => new Principal("alpha", "claude-two"),
            "fixture-other" => new Principal("beta", "claude"),
            _ => throw new ArgumentException("Unknown fixture identity")
        };
        var session = new McpSession(pipe, principal);
        var init = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
        Assert.NotNull(init);
        _ = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        return session;
    }
    private static async Task<(bool Error, string Text)> Call(McpSession session, string tool, object args) {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = args } });
        using var reply = JsonDocument.Parse((await session.HandleAsync(request))!);
        var result = reply.RootElement.GetProperty("result");
        return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }
    private async Task<string> Send(string body, string key = "message-key", string correlation = "conversation") {
        var (Error, Text) = await Call(codex, "pigeon_send", new { recipient = "claude", body, correlationId = correlation, idempotencyKey = key, expiresAtUnixMs = 100000L });
        Assert.False(Error);
        return JsonSerializer.Deserialize<string>(Text)!;
    }
    private async Task<JsonDocument> Receive(string? topic = null) {
        var (Error, Text) = await Call(claude, "pigeon_receive", new { topic });
        Assert.False(Error);
        return JsonDocument.Parse(Text);
    }
    private void SaveReceipt(JsonDocument message) {
        previousReceipt = receipt;
        receipt = message.RootElement.GetProperty("Receipt").GetString()!;
    }

    [Given("a shared service with registered Codex and Claude identities")]
    public void Registered() => Assert.NotNull(broker);

    [When("Codex registers a different session for its existing identity")]
    public async Task RegisterConflictingSession() {
        var (error, _) = await Call(codex, "pigeon_register", new { runtime = "mock", sessionId = "different-session" });
        lastError = error;
    }

    [When("Codex registers its original session again")]
    public async Task RegisterOriginalSession() {
        var (error, _) = await Call(codex, "pigeon_register", new { runtime = "mock", sessionId = "synthetic-test-session" });
        Assert.False(error);
    }

    [Then("the original Codex binding remains an unverified session claim")]
    public async Task OriginalBinding() {
        var (error, text) = await Call(codex, "pigeon_identity", new { });
        Assert.False(error);
        using var identity = JsonDocument.Parse(text);
        Assert.Equal("alpha", identity.RootElement.GetProperty("Project").GetString());
        Assert.Equal("codex", identity.RootElement.GetProperty("Agent").GetString());
        Assert.Equal("mock", identity.RootElement.GetProperty("Runtime").GetString());
        Assert.Equal("synthetic-test-session", identity.RootElement.GetProperty("SessionId").GetString());
        Assert.Equal("unknown", identity.RootElement.GetProperty("Origin").GetString());
        Assert.False(identity.RootElement.GetProperty("RuntimeVerified").GetBoolean());
    }

    [When("Codex records its current session as existing")]
    public async Task ClaimExistingOrigin() {
        var (error, _) = await Call(codex, "pigeon_register", new { runtime = "mock", sessionId = "synthetic-test-session", origin = "existing" });
        Assert.False(error);
    }

    [Then("its session origin is reported as existing and unverified")]
    public async Task VerifyOriginClaim() {
        var (error, text) = await Call(codex, "pigeon_identity", new { });
        Assert.False(error);
        using var identity = JsonDocument.Parse(text);
        Assert.Equal("existing", identity.RootElement.GetProperty("Origin").GetString());
        Assert.False(identity.RootElement.GetProperty("RuntimeVerified").GetBoolean());
    }

    [When("Codex claims the same session is newly launched")]
    public async Task ConflictingOrigin() {
        var (error, _) = await Call(codex, "pigeon_register", new { runtime = "mock", sessionId = "synthetic-test-session", origin = "new" });
        lastError = error;
    }

    [Given("the shared service has an enabled short body retention policy")]
    public async Task EnableRetention() {
        await StopService();
        retention = 1000;
        StartService();
    }

    [Given("the shared service has a project rate limit of two automated deliveries per minute")]
    public async Task EnableRateLimit() {
        await StopService();
        automatedRate = 2;
        StartService();
    }

    [When("Codex sends three automated messages in different exchanges")]
    public async Task SendDifferentExchanges() {
        for (var i = 0; i < 3; i++) {
            var (error, _) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "ping", correlationId = "rate-" + i, idempotencyKey = "rate-" + i, expiresAtUnixMs = 100000L });
            Assert.Equal(i == 2, error);
            lastError = error;
        }
    }

    [When("one minute elapses")]
    public void AdvanceRateWindow() => now += 60001;

    [Then("Codex can send another automated message")]
    public async Task SendAfterRateWindow() {
        var (error, _) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "ping", correlationId = "rate-2", idempotencyKey = "rate-2", expiresAtUnixMs = 100000L });
        Assert.False(error);
    }

    [When("the body retention window elapses")]
    public void AdvanceRetention() => now += 2000;

    [When("Codex inspects its identity")]
    public async Task InspectIdentity() {
        var (error, _) = await Call(codex, "pigeon_identity", new { });
        Assert.False(error);
    }

    [Then("the acknowledged message body is redacted by the service")]
    public void HostRedactedBody() {
        using var db = new Sqlite(Path.Combine(directory, "messages.db"));
        Assert.Equal("", db.Query("SELECT body FROM messages WHERE id=?", messageId)[0][0]);
    }

    [When("Codex sends {string} to Claude")]
    public async Task SendMessage(string body) => messageId = await Send(body);

    [When("another local client uses the Claude project and identity labels")]
    public async Task ClaimLabel() => impersonatingClient = await Connect("fixture-claude");

    [Then("that client can poll the message without a credential")]
    public async Task PollWithoutCredential() {
        var (error, text) = await Call(impersonatingClient, "pigeon_receive", new { });
        Assert.False(error);
        using var message = JsonDocument.Parse(text);
        Assert.Equal("ping", message.RootElement.GetProperty("Body").GetString());
    }

    [Then("Claude sees its {string} subscription and Codex does not")]
    public async Task InspectSubscriptions(string topic) {
        var (claudeError, claudeText) = await Call(claude, "pigeon_mailboxes", new { });
        var (codexError, codexText) = await Call(codex, "pigeon_mailboxes", new { });
        Assert.False(claudeError);
        Assert.False(codexError);
        Assert.Equal([topic], JsonSerializer.Deserialize<string[]>(claudeText)!);
        Assert.Empty(JsonSerializer.Deserialize<string[]>(codexText)!);
    }

    [Then("Claude sees one queued delivery and a paused project in inbox status")]
    public async Task ClaudeInboxStatus() {
        var (error, text) = await Call(claude, "pigeon_inbox_status", new { });
        Assert.False(error);
        using var status = JsonDocument.Parse(text);
        Assert.True(status.RootElement.GetProperty("ProjectPaused").GetBoolean());
        Assert.Equal(1, status.RootElement.GetProperty("Queued").GetInt32());
    }

    [Then("Codex sees no queued deliveries in inbox status")]
    public async Task CodexInboxStatus() {
        var (error, text) = await Call(codex, "pigeon_inbox_status", new { });
        Assert.False(error);
        using var status = JsonDocument.Parse(text);
        Assert.Equal(0, status.RootElement.GetProperty("Queued").GetInt32());
    }

    [When("Claude subscribes to twelve different mailboxes")]
    public async Task CreateActivityHistory() {
        for (var i = 0; i < 12; i++) {
            Assert.False((await Call(claude, "pigeon_topic_create", new { topic = "topic-" + i })).Error);
            var (error, _) = await Call(claude, "pigeon_subscribe", new { topic = "topic-" + i });
            Assert.False(error);
        }
    }

    [Then("project activity returns distinct bounded pages of history")]
    public async Task PaginatedActivity() {
        var (firstError, firstText) = await Call(codex, "pigeon_activity", new { limit = 5 });
        Assert.False(firstError);
        var first = JsonSerializer.Deserialize<string[]>(firstText)!;
        Assert.Equal(5, first.Length);
        var before = long.Parse(first[0].Split(" | ")[0]);
        var (secondError, secondText) = await Call(codex, "pigeon_activity", new { limit = 5, beforeSequence = before });
        Assert.False(secondError);
        var second = JsonSerializer.Deserialize<string[]>(secondText)!;
        Assert.Equal(5, second.Length);
        Assert.Empty(first.Intersect(second));
        Assert.All(second, line => Assert.True(long.Parse(line.Split(" | ")[0]) < before));
        var (tooMany, _) = await Call(codex, "pigeon_activity", new { limit = 11 });
        Assert.True(tooMany);
    }

    [When("another project records two subscription events")]
    public async Task OtherProjectActivity() {
        foreach (var topic in new[] { "one", "two" }) {
            Assert.False((await Call(other, "pigeon_topic_create", new { topic })).Error);
            var (error, _) = await Call(other, "pigeon_subscribe", new { topic });
            Assert.False(error);
        }
    }

    [Then("this project's activity cursors are contiguous")]
    public async Task ProjectLocalActivity() {
        var (error, text) = await Call(codex, "pigeon_activity", new { });
        Assert.False(error);
        var entries = JsonSerializer.Deserialize<string[]>(text)!;
        var ordinals = entries.Select(line => long.Parse(line.Split(" | ")[0])).ToArray();
        Assert.Equal(Enumerable.Range(1, ordinals.Length).Select(x => (long)x), ordinals);
    }

    [When("Codex sends a message that expires before its lease")]
    public async Task SendShortExpiry() {
        var (Error, Text) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "short", correlationId = "short", idempotencyKey = "short", expiresAtUnixMs = 1010L });
        Assert.False(Error);
        messageId = JsonSerializer.Deserialize<string>(Text)!;
    }

    [When("Codex sends {string} to Claude twice with the same idempotency key")]
    public async Task SendTwice(string body) {
        messageId = await Send(body);
        secondId = await Send(body);
    }

    [Then("both sends identify the same message")]
    public void SameMessage() => Assert.Equal(messageId, secondId);

    [When("Codex reuses the key with different content")]
    public async Task Conflict() {
        var (Error, _) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "changed", correlationId = "conversation", idempotencyKey = "message-key", expiresAtUnixMs = 100000L });
        lastError = Error;
    }

    [When("the shared service restarts")]
    public async Task Restart() {
        await StopService();
        StartService();
    }

    [When("the shared service stops")]
    public async Task Stop() => await StopService();

    [Then("Claude can poll {string}")]
    [Then("Claude can poll {string} again")]
    public async Task Poll(string body) {
        using var message = await Receive();
        Assert.Equal(body, message.RootElement.GetProperty("Body").GetString());
        SaveReceipt(message);
    }

    [When("Claude polls without acknowledging")]
    public async Task PollWithoutAck() {
        using var message = await Receive();
        SaveReceipt(message);
    }

    [When("Claude acknowledges the delivery")]
    public async Task Acknowledge() {
        var (Error, Text) = await Call(claude, "pigeon_acknowledge", new { receipt });
        Assert.False(Error);
        Assert.Equal("true", Text);
    }

    [Then("repeating the acknowledgment succeeds")]
    public async Task RepeatAcknowledgment() => await Acknowledge();

    [Then("the project activity records the acknowledgment")]
    public async Task AcknowledgmentInActivity() {
        var (Error, Text) = await Call(codex, "pigeon_activity", new { });
        Assert.False(Error);
        using var log = JsonDocument.Parse(Text);
        Assert.Contains(log.RootElement.EnumerateArray(), entry => entry.GetString()!.Contains("acknowledged"));
    }

    [Then("project activity records the {string} delivery")]
    public async Task TerminalInActivity(string state) {
        var (error, text) = await Call(codex, "pigeon_activity", new { });
        Assert.False(error);
        using var log = JsonDocument.Parse(text);
        Assert.Contains(log.RootElement.EnumerateArray(), entry => entry.GetString()!.Contains($" | {state} | {messageId} |"));
    }

    [When("Claude replies {string} to the received message")]
    public async Task Reply(string body) {
        var (Error, _) = await Call(claude, "pigeon_send", new { recipient = "codex", body, correlationId = "conversation", replyTo = messageId, idempotencyKey = "reply-key", expiresAtUnixMs = 100000L });
        Assert.False(Error);
    }

    [When("Claude replies using a different conversation identifier")]
    public async Task WrongCorrelation() {
        var (Error, _) = await Call(claude, "pigeon_send", new { recipient = "codex", body = "reply", correlationId = "different", replyTo = messageId, idempotencyKey = "wrong-reply", expiresAtUnixMs = 100000L });
        lastError = Error;
    }

    [Then("Codex polls {string} as a reply to the original message")]
    public async Task PollReply(string body) {
        var (Error, Text) = await Call(codex, "pigeon_receive", new { });
        Assert.False(Error);
        using var reply = JsonDocument.Parse(Text);
        Assert.Equal(body, reply.RootElement.GetProperty("Body").GetString());
        Assert.Equal(messageId, reply.RootElement.GetProperty("ReplyTo").GetString());
        Assert.Equal("conversation", reply.RootElement.GetProperty("CorrelationId").GetString());
    }

    [Then("the previous receipt is rejected")]
    [Then("the previous receipt is still rejected")]
    public async Task OldReceiptRejected() {
        var (Error, Text) = await Call(claude, "pigeon_acknowledge", new { receipt = previousReceipt });
        Assert.False(Error);
        Assert.Equal("false", Text);
    }

    [Then("the current receipt is rejected")]
    public async Task CurrentReceiptRejected() {
        var (Error, Text) = await Call(claude, "pigeon_acknowledge", new { receipt });
        Assert.False(Error);
        Assert.Equal("false", Text);
    }

    [When("Claude fails to acknowledge three delivery attempts")]
    public async Task ExhaustAttempts() {
        for (var i = 0; i < 3; i++) {
            using var message = await Receive();
            Assert.Equal("ping", message.RootElement.GetProperty("Body").GetString());
            now += 30001;
        }
    }

    [Then("Codex sees one timeout and one acknowledgment in the attempt history")]
    public async Task Attempts() {
        var (Error, Text) = await Call(codex, "pigeon_status", new { messageId });
        Assert.False(Error);
        using var status = JsonDocument.Parse(Text);
        var attempts = status.RootElement.GetProperty("Attempts");
        Assert.Equal(2, attempts.GetArrayLength());
        Assert.Equal("timed_out", attempts[0].GetProperty("Outcome").GetString());
        Assert.Equal("acknowledged", attempts[1].GetProperty("Outcome").GetString());
    }

    [Then("the attempt history records {string}")]
    public async Task AttemptOutcome(string outcome) {
        var (Error, Text) = await Call(codex, "pigeon_status", new { messageId });
        Assert.False(Error);
        using var status = JsonDocument.Parse(Text);
        Assert.Equal(outcome, status.RootElement.GetProperty("Attempts")[0].GetProperty("Outcome").GetString());
    }

    [Then("Codex sees the delivery as {string}")]
    public async Task Status(string state) {
        var (Error, Text) = await Call(codex, "pigeon_status", new { messageId });
        Assert.False(Error);
        using var status = JsonDocument.Parse(Text);
        Assert.Equal(state, status.RootElement.GetProperty("State").GetString());
    }

    [Then("Claude has no other message waiting")]
    public async Task EmptyInbox() {
        using var message = await Receive();
        Assert.Equal(JsonValueKind.Null, message.RootElement.ValueKind);
    }

    [When("time advances beyond the message expiry")]
    public void ExpireMessage() => now = 100001;

    [When("time advances beyond both expiry and lease")]
    public void ExpireBoth() => now = 32000;

    [When("time advances beyond the delivery lease")]
    public void ExpireLease() => now += 30001;

    [Then("the Claude identity in another project cannot inspect the message")]
    public async Task WrongProjectStatus() {
        var (Error, Text) = await Call(other, "pigeon_status", new { messageId });
        Assert.False(Error);
        Assert.Equal("null", Text);
    }

    [Then("the Claude identity in another project has an empty inbox")]
    public async Task WrongProjectInbox() {
        var (Error, Text) = await Call(other, "pigeon_receive", new { });
        Assert.False(Error);
        Assert.Equal("null", Text);
    }

    [Then("the Claude identity in another project cannot acknowledge the delivery")]
    public async Task WrongProjectAck() {
        var (Error, Text) = await Call(other, "pigeon_acknowledge", new { receipt });
        Assert.False(Error);
        Assert.Equal("false", Text);
    }

    [Then("the service refuses the request")]
    public void RequestRejected() => Assert.True(lastError);

    [Given("Claude subscribes to the {string} mailbox")]
    [When("Claude subscribes to the {string} mailbox")]
    public async Task Subscribe(string topic) {
        Assert.False((await Call(claude, "pigeon_topic_create", new { topic })).Error);
        Assert.False((await Call(claude, "pigeon_subscribe", new { topic })).Error);
    }

    [When("Codex creates the {string} topic")]
    public async Task CreateTopic(string topic) => Assert.False((await Call(codex, "pigeon_topic_create", new { topic })).Error);

    [Then("the topic is visible in Codex's project but not another project")]
    public async Task TopicScoped() {
        var (error, own) = await Call(codex, "pigeon_topics", new { });
        var (otherError, external) = await Call(other, "pigeon_topics", new { });
        Assert.False(error);
        Assert.False(otherError);
        Assert.Contains("design", JsonSerializer.Deserialize<string[]>(own)!);
        Assert.DoesNotContain("design", JsonSerializer.Deserialize<string[]>(external)!);
    }

    [When("Codex deletes the {string} topic")]
    public async Task DeleteTopic(string topic) {
        var (error, text) = await Call(codex, "pigeon_topic_delete", new { topic });
        Assert.False(error);
        Assert.Equal("true", text);
    }

    [Then("both subscribers are unsubscribed and receive a closure notice and the queued post")]
    public async Task DeletedTopicHistory() {
        var (error, topics) = await Call(codex, "pigeon_topics", new { });
        Assert.False(error);
        Assert.DoesNotContain("design", JsonSerializer.Deserialize<string[]>(topics)!);
        foreach (var subscriber in new[] { claude, secondClaude }) {
            var (mailError, mailboxes) = await Call(subscriber, "pigeon_mailboxes", new { });
            Assert.False(mailError);
            Assert.DoesNotContain("design", JsonSerializer.Deserialize<string[]>(mailboxes)!);
            var bodies = new List<string>();
            for (var i = 0; i < 2; i++) {
                var (receiveError, text) = await Call(subscriber, "pigeon_receive", new { topic = "design" });
                Assert.False(receiveError);
                using var item = JsonDocument.Parse(text);
                bodies.Add(item.RootElement.GetProperty("Body").GetString()!);
                Assert.False((await Call(subscriber, "pigeon_acknowledge", new { receipt = item.RootElement.GetProperty("Receipt").GetString() })).Error);
            }
            Assert.Contains("design note", bodies);
            Assert.Contains("Topic closed: design", bodies);
        }
    }

    [When("Claude subscribes to a nonexistent topic")]
    public async Task MissingTopic() => lastError = (await Call(claude, "pigeon_subscribe", new { topic = "missing" })).Error;

    [When("Codex publishes {string} to the {string} mailbox")]
    public async Task Publish(string body, string topic) {
        var (Error, Text) = await Call(codex, "pigeon_publish", new { topic, body, correlationId = "conversation", idempotencyKey = "publication-key", expiresAtUnixMs = 100000L });
        Assert.False(Error);
        using var ids = JsonDocument.Parse(Text);
        messageId = ids.RootElement[0].GetString()!;
    }

    [When("Codex publishes {string} to the {string} mailbox twice with the same key")]
    public async Task PublishTwice(string body, string topic) {
        await Publish(body, topic);
        var firstId = messageId;
        await Publish(body, topic);
        secondId = messageId;
        messageId = firstId;
    }

    [When("Codex publishes to a mailbox without subscribers")]
    public async Task PublishWithoutSubscribers() {
        Assert.False((await Call(codex, "pigeon_topic_create", new { topic = "empty" })).Error);
        var (error, text) = await Call(codex, "pigeon_publish", new { topic = "empty", body = "message" });
        Assert.False(error);
        Assert.Empty(JsonSerializer.Deserialize<string[]>(text)!);
    }

    [Then("Claude can pull the post without subscribing")]
    public async Task PullWithoutSubscription() {
        var (error, text) = await Call(claude, "pigeon_topic_read", new { topic = "empty" });
        Assert.False(error);
        using var posts = JsonDocument.Parse(text);
        Assert.Single(posts.RootElement.EnumerateArray());
        Assert.Equal("message", posts.RootElement[0].GetProperty("Body").GetString());
        Assert.DoesNotContain("empty", (await Call(claude, "pigeon_mailboxes", new { })).Text);
        Assert.Equal("[]", (await Call(codex, "pigeon_topic_read", new { topic = "empty" })).Text);
    }

    [When("Codex sends a direct message with only a retry key")]
    public async Task IncompleteRetryMetadata() {
        var result = await Call(codex, "pigeon_send", new { recipient = "claude", body = "test", idempotencyKey = "key" });
        lastError = result.Error;
    }

    [When("Codex publishes four posts to a new topic")]
    public async Task PublishFourPosts() {
        Assert.False((await Call(codex, "pigeon_topic_create", new { topic = "history" })).Error);
        for (var i = 0; i < 4; i++)
            Assert.False((await Call(codex, "pigeon_publish", new { topic = "history", body = "post " + i })).Error);
    }

    [Then("Claude reads the posts in sequence without subscribing")]
    public async Task ReadPagedHistory() {
        var first = await Call(claude, "pigeon_topic_read", new { topic = "history" });
        Assert.False(first.Error);
        using var page = JsonDocument.Parse(first.Text);
        Assert.Equal(3, page.RootElement.GetArrayLength());
        Assert.Equal("post 0", page.RootElement[0].GetProperty("Body").GetString());
        var cursor = page.RootElement[2].GetProperty("Sequence").GetInt64();
        var second = await Call(claude, "pigeon_topic_read", new { topic = "history", afterSequence = cursor });
        Assert.False(second.Error);
        using var next = JsonDocument.Parse(second.Text);
        Assert.Single(next.RootElement.EnumerateArray());
        Assert.Equal("post 3", next.RootElement[0].GetProperty("Body").GetString());
    }

    [Then("another project cannot read those posts")]
    public async Task CannotReadOtherProject() {
        var result = await Call(other, "pigeon_topic_read", new { topic = "history" });
        Assert.True(result.Error);
    }

    [Then("creating it again returns an existing topic warning")]
    public async Task DuplicateTopicWarning() {
        var (error, text) = await Call(codex, "pigeon_topic_create", new { topic = "design" });
        Assert.False(error);
        using var result = JsonDocument.Parse(text);
        Assert.False(result.RootElement.GetProperty("created").GetBoolean());
        Assert.Equal("Topic already exists", result.RootElement.GetProperty("warning").GetString());
    }

    [When("a second Claude session subscribes to the {string} mailbox")]
    [Given("a second Claude session subscribes to the {string} mailbox")]
    public async Task SecondSubscribe(string topic) => Assert.False((await Call(secondClaude, "pigeon_subscribe", new { topic })).Error);

    [Given("the Claude identity in another project subscribes to the {string} mailbox")]
    public async Task OtherSubscribe(string topic) {
        Assert.False((await Call(other, "pigeon_topic_create", new { topic })).Error);
        Assert.False((await Call(other, "pigeon_subscribe", new { topic })).Error);
    }

    [When("Claude unsubscribes from the {string} mailbox")]
    public async Task Unsubscribe(string topic) => Assert.False((await Call(claude, "pigeon_subscribe", new { topic, subscribed = false })).Error);

    [When("both Claude sessions poll the {string} mailbox")]
    public async Task BothPoll(string topic) {
        using var first = await Receive(topic);
        var (Error, Text) = await Call(secondClaude, "pigeon_receive", new { topic });
        Assert.False(Error);
        using var second = JsonDocument.Parse(Text);
        SaveReceipt(first);
        secondDeliveryId = second.RootElement.GetProperty("Id").GetString()!;
        Assert.NotEqual(first.RootElement.GetProperty("Id").GetString(), secondDeliveryId);
    }

    [Then("the second Claude delivery is still unacknowledged")]
    public async Task SecondUnacknowledged() {
        var (Error, Text) = await Call(codex, "pigeon_status", new { messageId = secondDeliveryId });
        Assert.False(Error);
        using var status = JsonDocument.Parse(Text);
        Assert.Equal("leased", status.RootElement.GetProperty("State").GetString());
    }

    [Then("the second Claude session has no older {string} message")]
    public async Task SecondInbox(string topic) {
        var (Error, Text) = await Call(secondClaude, "pigeon_receive", new { topic });
        Assert.False(Error);
        Assert.Equal("null", Text);
    }

    [Then("Claude can poll {string} from the {string} mailbox")]
    public async Task PollTopic(string body, string topic) {
        using var message = await Receive(topic);
        Assert.Equal(body, message.RootElement.GetProperty("Body").GetString());
        Assert.Equal(topic, message.RootElement.GetProperty("Topic").GetString());
    }

    [Then("the MCP connection reports that the shared service is unavailable")]
    public async Task ServiceUnavailable() {
        var (Error, Text) = await Call(codex, "pigeon_activity", new { });
        Assert.True(Error);
        Assert.Equal("Shared Pigeon service unavailable", Text);
    }

    [When("Codex sends five messages in one automated exchange")]
    public async Task FiveMessages() {
        for (var i = 0; i < 5; i++) {
            var (Error, _) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "message", correlationId = "bounded", idempotencyKey = "key-" + i, expiresAtUnixMs = 100000L });
            if (i < 4)
                Assert.False(Error);
            else
                lastError = Error;
        }
    }

    [When("Codex sends four messages in one automated exchange")]
    public async Task FourMessages() {
        for (var i = 0; i < 4; i++) {
            var (Error, _) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "message", correlationId = "bounded", idempotencyKey = "key-" + i, expiresAtUnixMs = 100000L });
            Assert.False(Error);
        }
    }

    [When("Codex publishes to the {string} mailbox in that exchange")]
    public async Task PublishInBoundedExchange(string topic) {
        var (Error, _) = await Call(codex, "pigeon_publish", new { topic, body = "message", correlationId = "bounded", idempotencyKey = "bounded-publication", expiresAtUnixMs = 100000L });
        lastError = Error;
    }

    [Then("the fifth message is rejected")]
    public void FifthRejected() => Assert.True(lastError);

    [When("Codex pauses the project")]
    public async Task Pause() => Assert.False((await Call(codex, "pigeon_pause", new { paused = true })).Error);

    [When("Codex resumes the project")]
    public async Task Resume() => Assert.False((await Call(codex, "pigeon_pause", new { paused = false })).Error);

    [Then("Codex cannot send a new message")]
    public async Task SendWhilePaused() {
        var (Error, _) = await Call(codex, "pigeon_send", new { recipient = "claude", body = "paused", correlationId = "pause", idempotencyKey = "pause-key", expiresAtUnixMs = 100000L });
        Assert.True(Error);
    }

    [Then("Codex can send a new message")]
    public async Task SendAfterResume() => Assert.NotEmpty(await Send("resumed", "resumed-key", "pause"));

    [When("Claude polls concurrently from two connections")]
    public async Task ConcurrentPoll() {
        var anotherConnection = await Connect("fixture-claude");
        var polls = await Task.WhenAll(Call(claude, "pigeon_receive", new { }), Call(anotherConnection, "pigeon_receive", new { }));
        Assert.All(polls, p => Assert.False(p.Error));
        receivedConcurrently = polls.Count(p => p.Text != "null");
    }

    [Then("only one poll receives the message")]
    public void OneLease() => Assert.Equal(1, receivedConcurrently);

    [When("two MCP processes connect to a separate shared service")]
    public async Task StartSeparateProcesses() {
        await StopService();
        await StartFixtureHost();
        codexProcess = StartFixture("fixture-mcp-default", pipe, "a");
        claudeProcess = StartFixture("fixture-mcp", pipe, "b");
        foreach (var (process, agent) in new[] { (codexProcess, "a"), (claudeProcess, "b") }) {
            await SendLine(process, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25" } });
            Assert.Contains("protocolVersion", await ReadLine(process));
            await SendLine(process, new { jsonrpc = "2.0", method = "notifications/initialized" });
            Assert.False((await ProcessCall(process, "pigeon_register", new { agent, runtime = "mock", sessionId = $"synthetic-test-session-{agent}" })).Error);
        }
        Assert.False((await ProcessCall(codexProcess, "pigeon_topic_create", new { topic = "fixture-topic" })).Error);
        Assert.False((await ProcessCall(codexProcess, "pigeon_subscribe", new { topic = "fixture-topic" })).Error);
        Assert.False((await ProcessCall(claudeProcess, "pigeon_subscribe", new { topic = "fixture-topic" })).Error);
    }

    [When("two MCP processes connect to a service with an automated exchange limit of one")]
    public async Task StartLimitedProcesses() {
        fixtureLimited = true;
        await StartSeparateProcesses();
    }

    [When("Codex sends another message in the same exchange through its MCP process")]
    public async Task SendPastFixtureLimit() {
        var (error, _) = await ProcessCall(codexProcess!, "pigeon_send", new { recipient = "b", body = "second", correlationId = "process-test", idempotencyKey = "second-key", expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds() });
        lastError = error;
    }

    [When("Codex sends {string} through its MCP process")]
    public async Task SendThroughProcess(string body) {
        var (Error, _) = await ProcessCall(codexProcess!, "pigeon_send", new { recipient = "b", body, correlationId = "process-test", idempotencyKey = "process-key", expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds() });
        Assert.False(Error);
    }

    [When("the separate shared service restarts")]
    public async Task RestartFixtureHost() {
        await StopFixtureHost();
        await StartFixtureHost();
    }

    [When("another service attempts to use the same pipe")]
    public async Task DuplicateHost() {
        using var duplicate = StartFixture("fixture-host", pipe, Path.Combine(directory, "other-fixture.db"));
        try {
            await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            secondHostRejected = duplicate.ExitCode != 0;
        }
        finally {
            if (!duplicate.HasExited) {
                duplicate.Kill();
                duplicate.WaitForExit();
            }
        }
    }

    [Then("the second service refuses to start")]
    public void DuplicateRejected() => Assert.True(secondHostRejected);

    [Then("Claude polls {string} through its MCP process")]
    public async Task PollThroughProcess(string body) {
        var (Error, Text) = await ProcessCall(claudeProcess!, "pigeon_receive", new { });
        Assert.False(Error);
        using var message = JsonDocument.Parse(Text);
        Assert.Equal(body, message.RootElement.GetProperty("Body").GetString());
        processReceipt = message.RootElement.GetProperty("Receipt").GetString()!;
    }

    [Then("Claude acknowledges through its MCP process")]
    public async Task AckThroughProcess() {
        var (Error, Text) = await ProcessCall(claudeProcess!, "pigeon_acknowledge", new { receipt = processReceipt });
        Assert.False(Error);
        Assert.Equal("true", Text);
    }

    [Then("the status command reports the service without message bodies")]
    public async Task CliStatus() {
        using var status = StartFixture("fixture-status", pipe, "a");
        var output = await status.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await status.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Pigeon service: running", output);
        Assert.Contains("agent session liveness not verified", output);
        Assert.DoesNotContain("activity ping", output);
    }

    [Then("the status command reports the running service")]
    public async Task CliUnregistered() {
        using var status = StartFixture("fixture-status", pipe, "c");
        var output = await status.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await status.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("Pigeon service: running", output);
    }

    [Then("status shows both active MCP bridges")]
    public async Task TwoBridges() => await ExpectBridgeCount(2);

    [When("the Claude MCP process disconnects")]
    public async Task DisconnectClaudeBridge() {
        claudeProcess!.StandardInput.Close();
        await claudeProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Then("status shows only the remaining MCP bridge")]
    public async Task OneBridge() => await ExpectBridgeCount(1);

    private async Task ExpectBridgeCount(int count) {
        for (var i = 0; i < 30; i++) {
            using var status = StartFixture("fixture-status", pipe, "a");
            var output = await status.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await status.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (output.Contains($"MCP bridges: {count} recently active")) {
                Assert.Contains("agent session liveness not verified", output);
                return;
            }
            await Task.Delay(250);
        }
        throw new Xunit.Sdk.XunitException($"Expected {count} active test bridges.");
    }

    private async Task<string> RunCli(string identity, string command, string? input, params string[] arguments) {
        var fixtureArgs = new List<string> { "fixture-group", pipe, identity };
        switch (command) {
            case "send":
                fixtureArgs.AddRange(["messages", "direct", identity, arguments[0], "-", "--correlation-id", arguments[1], "--idempotency-key", arguments[2], "--expires-at-unix-ms", arguments[3]]);
                if (arguments.Length == 5) fixtureArgs.AddRange(["--reply-to", arguments[4]]);
                break;
            case "publish":
                fixtureArgs.AddRange(["messages", "send", identity, arguments[0], "-", "--correlation-id", arguments[1], "--idempotency-key", arguments[2], "--expires-at-unix-ms", arguments[3]]);
                break;
            case "receive":
                fixtureArgs.AddRange(["messages", "get", identity]);
                fixtureArgs.AddRange(arguments);
                break;
            case "acknowledge": fixtureArgs.AddRange(["messages", "ack", identity, arguments[0]]); break;
            case "message-status": fixtureArgs.AddRange(["messages", "status", identity, arguments[0]]); break;
            case "subscribe": fixtureArgs.AddRange(["topics", "subscribe", arguments[0], identity, "mock", $"synthetic-test-session-{identity}"]); break;
            default: throw new ArgumentException("Unknown test operation");
        }
        using var process = StartFixture([.. fixtureArgs]);
        if (input is not null)
            await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"CLI failed: {error}");
        return output.Trim();
    }

    [When("Codex sends {string} to Claude from the CLI")]
    public async Task SendFromCli(string body) {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds().ToString();
        var output = await RunCli("a", "send", body, "b", "cli-conversation", "cli-key", expiry);
        messageId = JsonSerializer.Deserialize<string>(output)!;
        Assert.False(string.IsNullOrEmpty(messageId));
    }

    [Then("Claude receives {string} from the CLI")]
    public async Task ReceiveFromCli(string body) {
        var output = await RunCli("b", "receive", null);
        using var message = JsonDocument.Parse(output);
        Assert.Single(message.RootElement.EnumerateArray());
        Assert.Equal(body, message.RootElement[0].GetProperty("Body").GetString());
        processReceipt = message.RootElement[0].GetProperty("Receipt").GetString()!;
    }

    [When("Claude acknowledges the CLI delivery")]
    public async Task AcknowledgeFromCli() => Assert.Equal("Acknowledged.", await RunCli("b", "acknowledge", null, processReceipt));

    [Then("Codex sees the CLI delivery as acknowledged")]
    public async Task StatusFromCli() {
        var output = await RunCli("a", "message-status", null, messageId);
        using var status = JsonDocument.Parse(output);
        Assert.Equal("acknowledged", status.RootElement.GetProperty("State").GetString());
    }

    [When("Claude replies {string} from the CLI")]
    public async Task ReplyFromCli(string body) {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds().ToString();
        var output = await RunCli("b", "send", body, "a", "cli-conversation", "cli-reply", expiry, messageId);
        Assert.False(string.IsNullOrEmpty(JsonSerializer.Deserialize<string>(output)));
    }

    [Then("Codex receives the correlated CLI reply {string}")]
    public async Task ReceiveReplyFromCli(string body) {
        using var message = JsonDocument.Parse(await RunCli("a", "receive", null));
        Assert.Single(message.RootElement.EnumerateArray());
        Assert.Equal(body, message.RootElement[0].GetProperty("Body").GetString());
        Assert.Equal(messageId, message.RootElement[0].GetProperty("ReplyTo").GetString());
        Assert.Equal("cli-conversation", message.RootElement[0].GetProperty("CorrelationId").GetString());
    }

    [When("Codex retries the same direct CLI message")]
    public async Task RetryDirectFromCli() {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds().ToString();
        var first = await RunCli("a", "send", "retry body", "b", "retry-correlation", "retry-key", expiry);
        var retry = await RunCli("a", "send", "retry body", "b", "retry-correlation", "retry-key", expiry);
        Assert.Equal(first, retry);
    }

    [Then("Claude gets exactly one matching direct CLI delivery")]
    public async Task GetRetriedDirectFromCli() {
        using var messages = JsonDocument.Parse(await RunCli("b", "receive", null));
        Assert.Single(messages.RootElement.EnumerateArray());
        Assert.Equal("retry body", messages.RootElement[0].GetProperty("Body").GetString());
        Assert.Equal("retry-correlation", messages.RootElement[0].GetProperty("CorrelationId").GetString());
    }

    [When("Claude subscribes to {string} from the CLI")]
    public async Task SubscribeFromCli(string topic) {
        Assert.False((await ProcessCall(claudeProcess!, "pigeon_topic_create", new { topic })).Error);
        _ = await RunCli("b", "subscribe", null, topic, "on");
    }

    [When("Codex publishes {string} to {string} from the CLI")]
    public async Task PublishFromCli(string body, string topic) {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds().ToString();
        var output = await RunCli("a", "publish", body, topic, "cli-topic", "cli-topic-key", expiry);
        Assert.Single(JsonSerializer.Deserialize<string[]>(output)!);
    }

    [Then("Claude receives {string} from the {string} mailbox using the CLI")]
    public async Task ReceiveTopicFromCli(string body, string topic) {
        var output = await RunCli("b", "receive", null, topic);
        using var message = JsonDocument.Parse(output);
        Assert.Single(message.RootElement.EnumerateArray());
        Assert.Equal(body, message.RootElement[0].GetProperty("Body").GetString());
        Assert.Equal(topic, message.RootElement[0].GetProperty("Topic").GetString());
    }

    private async Task<string> RunGroup(string command, params string[] arguments) {
        var fixtureArgs = new List<string> { "fixture-group", pipe, "operator", command };
        fixtureArgs.AddRange(arguments);
        using var process = StartFixture([.. fixtureArgs]);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        var output = await stdout;
        var error = await stderr;
        Assert.True(process.ExitCode == 0, $"CLI failed: {error}");
        return output.Trim();
    }

    [When("the CLI creates the {string} topic")]
    public async Task CreateFromGroup(string topic) => _ = await RunGroup("topics", "create", topic);

    [When("the CLI subscribes {string} using {string} session {string} to {string}")]
    public async Task JoinFromGroup(string name, string runtime, string session, string topic) =>
        _ = await RunGroup("topics", "subscribe", topic, name, runtime, session);

    [When("{string} sends {string} to {string} using the grouped CLI")]
    public async Task SendFromGroup(string sender, string body, string topic) {
        using var ids = JsonDocument.Parse(await RunGroup("messages", "send", sender, topic, body));
        Assert.Equal(1, ids.RootElement.GetArrayLength());
    }

    [Then("{string} gets {string} from {string} using the grouped CLI")]
    public async Task GetFromGroup(string reader, string body, string topic) {
        using var messages = JsonDocument.Parse(await RunGroup("messages", "get", reader, topic));
        Assert.Single(messages.RootElement.EnumerateArray());
        var message = messages.RootElement[0];
        Assert.Equal(body, message.GetProperty("Body").GetString());
        Assert.Equal("ana", message.GetProperty("Sender").GetString());
        processReceipt = message.GetProperty("Receipt").GetString()!;
        messageId = message.GetProperty("Id").GetString()!;
    }

    [Then("that delivery is not acknowledged until Bob acknowledges its receipt")]
    public async Task GroupAck() {
        using (var before = JsonDocument.Parse(await RunCli("bob", "message-status", null, messageId)))
            Assert.Equal("leased", before.RootElement.GetProperty("State").GetString());
        Assert.Equal("Acknowledged.", await RunGroup("messages", "ack", "bob", processReceipt));
        using var after = JsonDocument.Parse(await RunCli("bob", "message-status", null, messageId));
        Assert.Equal("acknowledged", after.RootElement.GetProperty("State").GetString());
    }

    [When("a test Claude channel connects for its registered session")]
    public Task ConnectClaudeChannel() => ConnectChannel("fixture-channel");

    private async Task ConnectChannel(string fixture) {
        channelProcess = StartFixture(fixture, pipe, "c");
        await SendLine(channelProcess, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25" } });
        using var init = JsonDocument.Parse(await ReadLine(channelProcess));
        Assert.True(init.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("experimental").TryGetProperty("claude/channel", out _));
        await SendLine(channelProcess, new { jsonrpc = "2.0", method = "notifications/initialized" });
        var (error, _) = await ProcessCall(channelProcess, "pigeon_register", new { agent = "c", runtime = "claude_code", sessionId = "channel-test" });
        Assert.False(error);
        Assert.False((await ProcessCall(channelProcess, "pigeon_subscribe", new { topic = "fixture-topic" })).Error);
    }


    [When("Codex sends {string} to the channel identity")]
    public async Task SendToChannel(string body) {
        var (error, text) = await ProcessCall(codexProcess!, "pigeon_send", new { recipient = "c", body, correlationId = "channel-test", idempotencyKey = "channel-ping", expiresAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds() });
        Assert.False(error);
        messageId = JsonSerializer.Deserialize<string>(text)!;
    }

    [Then("the channel transport receives the matching wake")]
    public async Task ChannelWake() {
        using var notification = JsonDocument.Parse(await ReadLine(channelProcess!));
        Assert.Equal("notifications/claude/channel", notification.RootElement.GetProperty("method").GetString());
        var payload = notification.RootElement.GetProperty("params");
        Assert.Contains("pigeon_wake_confirm", payload.GetProperty("content").GetString());
        wakeGeneration = payload.GetProperty("meta").GetProperty("generation").GetString()!;
        Assert.True(Guid.TryParseExact(wakeGeneration, "N", out _));
    }

    [Then("the delivery remains queued until the channel client polls and acknowledges")]
    public async Task ChannelDoesNotAcknowledge() {
        var (statusError, statusText) = await ProcessCall(codexProcess!, "pigeon_status", new { messageId });
        Assert.False(statusError);
        using (var status = JsonDocument.Parse(statusText))
            Assert.Equal("queued", status.RootElement.GetProperty("State").GetString());
        var (confirmError, confirmText) = await ProcessCall(channelProcess!, "pigeon_wake_confirm", new { generation = wakeGeneration });
        Assert.False(confirmError);
        using (var confirm = JsonDocument.Parse(confirmText)) Assert.True(confirm.RootElement.GetProperty("Accepted").GetBoolean());
        var (receiveError, text) = await ProcessCall(channelProcess!, "pigeon_receive", new { });
        Assert.False(receiveError);
        using var message = JsonDocument.Parse(text);
        var receiptValue = message.RootElement.GetProperty("Receipt").GetString();
        var (ackError, ackText) = await ProcessCall(channelProcess!, "pigeon_acknowledge", new { receipt = receiptValue });
        Assert.False(ackError);
        Assert.Equal("true", ackText);
        var (finishError, finishText) = await ProcessCall(channelProcess!, "pigeon_wake_complete", new { generation = wakeGeneration });
        Assert.False(finishError);
        Assert.Equal("true", finishText);
    }

    private async Task StartFixtureHost() {
        fixtureHost = StartFixture(fixtureLimited ? "fixture-host-limited" : "fixture-host", pipe, Path.Combine(directory, "fixture.db"));
        Assert.StartsWith("Pigeon service ready", await ReadLine(fixtureHost));
    }
    private async Task StopFixtureHost() {
        if (fixtureHost is null)
            return;
        if (!fixtureHost.HasExited) {
            await fixtureHost.StandardInput.WriteLineAsync("stop");
            await fixtureHost.StandardInput.FlushAsync();
            if (!fixtureHost.WaitForExit(5000)) {
                fixtureHost.Kill();
                fixtureHost.WaitForExit();
            }
        }

        fixtureHost.Dispose();
        fixtureHost = null;
    }
    private static System.Diagnostics.Process StartFixture(params string[] args) {
        var output = Path.GetDirectoryName(typeof(MessagingSteps).Assembly.Location)!;
        var info = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("--runtimeconfig");
        info.ArgumentList.Add(Path.Combine(output, "Pigeon.IntegrationTests.runtimeconfig.json"));
        info.ArgumentList.Add("--depsfile");
        info.ArgumentList.Add(Path.Combine(output, "Pigeon.IntegrationTests.deps.json"));
        info.ArgumentList.Add(Path.Combine(output, "pigeon.dll"));
        info.Environment.Remove("PIGEON_BODY_RETENTION_DAYS");
        info.Environment.Remove("PIGEON_MAX_ATTEMPTS");
        info.Environment.Remove("PIGEON_MAX_AUTOMATED");
        info.Environment.Remove("PIGEON_MAX_AUTOMATED_PER_MINUTE");
        if (args[0].StartsWith("fixture-host", StringComparison.Ordinal)) {
            info.ArgumentList.Add("start");
            info.ArgumentList.Add("--foreground");
            info.Environment["PIGEON_PORT"] = args[1];
            info.Environment["PIGEON_DATABASE"] = args[2];
            if (args[0] == "fixture-host-limited")
                info.Environment["PIGEON_MAX_AUTOMATED"] = "1";
        }
        else {
            info.ArgumentList.Add(args[0] switch { "fixture-status" => "status", "fixture-group" => args[3], _ => "mcp" });
            if (args[0] is not ("fixture-group" or "fixture-mcp-default" or "fixture-channel"))
                info.ArgumentList.Add(args[1]);
            if (args[0] == "fixture-group") {
                foreach (var arg in args.Skip(4))
                    info.ArgumentList.Add(arg);
            }
            if (args[0].StartsWith("fixture-channel", StringComparison.Ordinal)) {
                info.ArgumentList.Add("--claude-channel");
                info.Environment["PIGEON_SESSION_ID"] = args[0] == "fixture-channel" ? "channel-test" : "different-session";
            }
            info.Environment["PIGEON_PORT"] = args[1];
        }

        return System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Could not start test fixture");
    }
    private static async Task SendLine(System.Diagnostics.Process process, object message) {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }
    private static async Task<string> ReadLine(System.Diagnostics.Process process) => await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("Test fixture exited unexpectedly");
    private static async Task<(bool Error, string Text)> ProcessCall(System.Diagnostics.Process process, string tool, object args) {
        await SendLine(process, new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = args } });
        using var reply = JsonDocument.Parse(await ReadLine(process));
        var result = reply.RootElement.GetProperty("result");
        return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
    }
}
