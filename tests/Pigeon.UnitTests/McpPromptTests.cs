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
