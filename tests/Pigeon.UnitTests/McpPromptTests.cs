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
        static async Task<bool> CanPrompt(string? runtime, string capabilities) {
            var session = new McpSession("1", runtime);
            _ = await session.HandleAsync($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{capabilities}}}}}");
            return session.CanPrompt;
        }

        Assert.True(await CanPrompt("claude-code", "{\"elicitation\":{}}"));
        Assert.False(await CanPrompt("claude-code", "{}"));
        Assert.False(await CanPrompt("antigravity", "{\"elicitation\":{\"form\":{}}}"));
    }
}
