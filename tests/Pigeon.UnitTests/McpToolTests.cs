using System.Text.Json;

using Pigeon.Service;

using Xunit;

namespace Pigeon.UnitTests;

// The tools' parameters, and which session a Claude Code connection joins as.
public sealed class McpToolTests {
    private const string Environment = "c1122145-145c-4af1-b02b-c2731e423fed", Hook = "5b0e2c1a-7f3d-4e8a-9c6b-1d2e3f4a5b6c";

    private static async Task<JsonElement[]> ToolList(McpSession session) {
        using var list = JsonDocument.Parse((await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/list\"}"))!);
        return [.. list.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.Clone())];
    }

    private static string[] Required(JsonElement[] tools, string name)
        => [.. tools.Single(t => t.GetProperty("name").GetString() == name).GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(r => r.GetString()!)];

    // A Claude Code connection; the service knows no session yet, so restoring binds nothing.
    private static async Task<(FakeService Service, McpSession Session)> Claude(string? environment = Environment) {
        var service = new FakeService().Answer("join", args => new { Topic = args.GetProperty("topic").GetString() });
        return (service, await McpHost.Ready(new McpSession("1", "claude-code", environment, service: service.Call)));
    }

    private static string? JoinedAs(FakeService service) => service.CallsTo("join").Last().GetProperty("sessionId").GetString();

    // Found in the 0.7.0-beta.3 test: sessions allowed only Pigeon's tools could not run the shell
    // command the connect skill used to read the session ID, so the agent never joined.
    [Fact]
    public async Task A_Claude_Code_join_without_a_session_ID_uses_the_one_Claude_Code_gave_the_server() {
        var (service, session) = await Claude();
        Assert.Equal(["topic"], Required(await ToolList(session), "pigeon_join"));
        var (error, text) = await McpHost.Call(session, "pigeon_join", new { topic = "review" });
        Assert.False(error, text);
        Assert.Equal(Environment, JoinedAs(service));
        Assert.Equal(Environment, session.SessionId);
    }

    [Fact]
    public async Task The_session_the_prompt_hook_named_last_comes_before_the_environments() {
        var (service, session) = await Claude();
        _ = await McpHost.Call(session, "pigeon_session_restore", new { tool = "claude-code", sessionId = "2d4c6e8a-0000-4000-8000-000000000001" });
        _ = await McpHost.Call(session, "pigeon_session_restore", new { tool = "claude-code", sessionId = Hook });
        var (error, text) = await McpHost.Call(session, "pigeon_join", new { topic = "review" });
        Assert.False(error, text);
        Assert.Equal(Hook, JoinedAs(service));
    }

    [Fact]
    public async Task An_explicit_session_ID_still_wins() {
        var (service, session) = await Claude();
        _ = await McpHost.Call(session, "pigeon_session_restore", new { tool = "claude-code", sessionId = Hook });
        var (error, text) = await McpHost.Call(session, "pigeon_join", new { topic = "review", sessionId = "explicit-session" });
        Assert.False(error, text);
        Assert.Equal("explicit-session", JoinedAs(service));
    }

    [Fact]
    public async Task Without_an_ID_from_the_host_a_session_ID_is_still_required() {
        var (service, session) = await Claude(environment: null);
        Assert.Equal(["topic", "sessionId"], Required(await ToolList(session), "pigeon_join"));
        var codex = await McpHost.Ready(new McpSession("1", "codex", Environment, service: service.Call));
        Assert.Equal(["topic", "sessionId"], Required(await ToolList(codex), "pigeon_join"));
        var (error, text) = await McpHost.Call(codex, "pigeon_join", new { topic = "review" });
        Assert.True(error);
        Assert.StartsWith("pigeon_join needs sessionId. Its parameters: topic (required, ", text);
        Assert.Empty(service.CallsTo("join"));
    }

    // Found in the 0.7.0-beta.3 test: Claude Code loads tool schemas late, so an agent's first call
    // guessed names and got "Pass a valid session ID." or "invalid request".
    [Fact]
    public async Task Wrong_parameters_are_answered_with_the_tools_own_parameter_list_and_no_service_call() {
        var service = new FakeService();
        var session = await McpHost.Ready(new McpSession("1", "codex", service: service.Call));
        async Task<string> Refused(string tool, object args) {
            var (error, text) = await McpHost.Call(session, tool, args);
            Assert.True(error, text);
            return text;
        }

        Assert.Equal("pigeon_receive does not take topic. Its parameters: generation (required, the generation named in the wake instruction).", await Refused("pigeon_receive", new { topic = "review" }));
        Assert.StartsWith("pigeon_join does not take session_id. Its parameters: topic (required, ", await Refused("pigeon_join", new { topic = "review", session_id = Environment }));
        Assert.StartsWith("pigeon_send needs message. Its parameters: ", await Refused("pigeon_send", new { topic = "review" }));
        Assert.StartsWith("pigeon_send takes topic as a string. Its parameters: ", await Refused("pigeon_send", new { topic = 5, message = "hello" }));
        Assert.StartsWith("pigeon_join takes remote as a boolean. ", await Refused("pigeon_join", new { topic = "review", sessionId = Environment, remote = "yes" }));
        Assert.Empty(service.Calls);
    }
}
