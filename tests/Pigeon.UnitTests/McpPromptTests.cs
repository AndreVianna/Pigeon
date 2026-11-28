using System.Text.Json;

using Pigeon.Service;

using Xunit;

namespace Pigeon.UnitTests;

// The MCP side of prompts: what Pigeon sends a host, and how it reads the person's answer.
public sealed class McpPromptTests {
    [Fact]
    public void A_prompt_is_an_elicitation_request_with_a_code_field_only_when_a_code_is_needed() {
        var withCode = JsonDocument.Parse(McpSession.PromptRequest("abc:code", "Enter the code", true)).RootElement;
        Assert.Equal("elicitation/create", withCode.GetProperty("method").GetString());
        Assert.Equal("pigeon-prompt:abc:code", withCode.GetProperty("id").GetString());
        Assert.Equal("code", withCode.GetProperty("params").GetProperty("requestedSchema").GetProperty("required")[0].GetString());
        var plain = JsonDocument.Parse(McpSession.PromptRequest("abc:allow", "Allow?", false)).RootElement;
        Assert.Empty(plain.GetProperty("params").GetProperty("requestedSchema").GetProperty("properties").EnumerateObject());
    }

    [Fact]
    public void Answers_are_read_from_replies_to_Pigeon_prompts_only() {
        Assert.Equal(("abc:code", "accept", "123456"), McpSession.PromptReply("{\"jsonrpc\":\"2.0\",\"id\":\"pigeon-prompt:abc:code\",\"result\":{\"action\":\"accept\",\"content\":{\"code\":\"123456\"}}}")!.Value);
        // An error reply, as from a host that cannot show the prompt, counts as cancel.
        Assert.Equal(("abc:allow", "cancel", (string?)null), McpSession.PromptReply("{\"jsonrpc\":\"2.0\",\"id\":\"pigeon-prompt:abc:allow\",\"error\":{\"code\":-32601,\"message\":\"no\"}}")!.Value);
        Assert.Null(McpSession.PromptReply("{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{}}"));
        Assert.Null(McpSession.PromptReply("{\"jsonrpc\":\"2.0\",\"id\":\"pigeon-prompt:x\",\"method\":\"tools/call\"}"));
        Assert.Null(McpSession.PromptReply("not json"));
    }

    // Found in the 0.7.0-beta.3 test: a join code entered with `pigeon allow` in a terminal completed
    // the join, but the Claude prompt asking for it stayed on screen two wakes later.
    [Fact]
    public async Task A_prompt_whose_request_ended_elsewhere_is_withdrawn_and_a_late_reply_is_not_passed_on() {
        const string session = "c1122145-145c-4af1-b02b-c2731e423fed";
        var offered = 0;
        var service = new FakeService()
            .Answer("session_restore", _ => new { SessionId = session })
            .Answer("prompts_next", _ => Interlocked.Increment(ref offered) == 1 ? new[] { new { id = "r1:code", message = "Pigeon: enter the join code.", code = true, show = (string?)null } } : [])
            .Answer("prompts_open", _ => Array.Empty<string>());
        using var input = new HostInput();
        var output = new HostOutput();
        var running = McpStdio.RunAsync(input, output, new McpSession("1", "claude-code", session, service: service.Call), TestContext.Current.CancellationToken, delay: McpHost.Quickly);
        input.Send(McpHost.Initialize);
        input.Send(McpHost.Initialized);

        var prompt = output.Next(m => m.TryGetProperty("method", out var method) && method.GetString() == "elicitation/create");
        Assert.Equal("pigeon-prompt:r1:code", prompt.GetProperty("id").GetString());
        // The MCP notification for a request the server sent: it names the request, and has no ID of its own.
        var withdrawn = output.Next(m => m.TryGetProperty("method", out var method) && method.GetString() == "notifications/cancelled");
        Assert.Equal("pigeon-prompt:r1:code", withdrawn.GetProperty("params").GetProperty("requestId").GetString());
        Assert.False(withdrawn.TryGetProperty("id", out _));

        input.Send("{\"jsonrpc\":\"2.0\",\"id\":\"pigeon-prompt:r1:code\",\"result\":{\"action\":\"accept\",\"content\":{\"code\":\"123456\"}}}");
        // Lines are handled in order, so the late reply has been handled once the ping is answered.
        input.Send("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"ping\"}");
        _ = output.Next(m => m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.GetInt32() == 5);
        Assert.Empty(service.CallsTo("prompt_answer"));
        input.End();
        await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_host_is_asked_only_when_it_supports_elicitation_and_is_not_Antigravity() {
        static async Task<bool> CanPrompt(string? runtime, string capabilities, bool hostShowsPrompts = true) {
            var session = new McpSession("1", runtime, hostShowsPrompts: hostShowsPrompts);
            _ = await session.HandleAsync($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{capabilities}}}}}");
            return session.CanPrompt;
        }

        Assert.True(await CanPrompt("claude-code", "{\"elicitation\":{}}"));
        Assert.False(await CanPrompt("claude-code", "{}"));
        Assert.False(await CanPrompt("antigravity", "{\"elicitation\":{\"form\":{}}}"));
        // Claude sessions started by the Claude desktop app advertise elicitation but cancel it unseen.
        Assert.False(await CanPrompt("claude-code", "{\"elicitation\":{}}", hostShowsPrompts: false));
    }

    // Found in the two-machine test on 2026-11-07: Claude Desktop refused every contact without showing
    // a prompt. Its person now answers in a terminal, and the agent is told so.
    [Fact]
    public void A_join_from_a_host_that_cannot_show_prompts_points_its_person_to_the_terminal() {
        var directory = Path.Combine(Path.GetTempPath(), "pigeon-prompts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            using var broker = new Broker(Path.Combine(directory, "messages.db"));
            broker.SetPeerSetting("listen", "127.0.0.1:49999");
            string Text(object args) {
                var result = broker.Execute("join", JsonSerializer.SerializeToElement(args));
                Assert.True(result.Ok, result.Error);
                return JsonSerializer.SerializeToElement(result.Value).GetProperty("text").GetString()!;
            }

            Assert.Contains("answer in a terminal: pigeon requests", Text(new { tool = "claude-code", sessionId = "desktop-session", topic = "t1@192.168.2.20", prompts = false }));
            Assert.DoesNotContain("pigeon requests", Text(new { tool = "claude-code", sessionId = "terminal-session", topic = "t2@192.168.2.20", prompts = true }));
            // The command line sends no flag: its person is at a terminal already.
            Assert.DoesNotContain("pigeon requests", Text(new { tool = "cli", sessionId = "cli-session", topic = "t3@192.168.2.20" }));
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}
