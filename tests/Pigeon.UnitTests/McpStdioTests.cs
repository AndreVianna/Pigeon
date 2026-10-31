using Pigeon.Service;
using Pigeon.Core;

using Xunit;

namespace Pigeon.UnitTests;

public class McpStdioTests {
    [Fact]
    public async Task Initialization_and_tool_discovery_do_not_require_a_service_connection() {
        var session = new McpSession("9", new Principal("test-project", "test-agent"));
        var init = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}", TestContext.Current.CancellationToken);
        Assert.Contains("protocolVersion", init);
        var notification = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", TestContext.Current.CancellationToken);
        Assert.Null(notification);
        var tools = await session.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}", TestContext.Current.CancellationToken);
        Assert.Contains("pigeon_receive", tools);
    }

    [Fact]
    public async Task Rejects_an_oversized_message_without_processing_it() {
        using var input = new StringReader(new string('a', 65537) + "\n");
        using var output = new StringWriter();
        var session = new McpSession("9", new Principal("test-project", "test-agent"));

        _ = await Assert.ThrowsAsync<InvalidDataException>(() => McpStdio.RunAsync(input, output, session, TestContext.Current.CancellationToken));
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task Rejects_a_truncated_message() {
        using var input = new StringReader("{\"jsonrpc\":\"2.0\"");
        using var output = new StringWriter();
        var session = new McpSession("9", new Principal("test-project", "test-agent"));

        _ = await Assert.ThrowsAsync<InvalidDataException>(() => McpStdio.RunAsync(input, output, session, TestContext.Current.CancellationToken));
        Assert.Equal("", output.ToString());
    }
}
