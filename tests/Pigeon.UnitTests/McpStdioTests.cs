using Pigeon.Service;

using Xunit;

namespace Pigeon.UnitTests;

public class McpStdioTests {
    [Fact]
    public async Task Rejects_an_oversized_message_without_processing_it() {
        using var input = new StringReader(new string('a', 65537) + "\n");
        using var output = new StringWriter();
        var session = new McpSession("9");

        _ = await Assert.ThrowsAsync<InvalidDataException>(() => McpStdio.RunAsync(input, output, session, TestContext.Current.CancellationToken));
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public async Task Rejects_a_truncated_message() {
        using var input = new StringReader("{\"jsonrpc\":\"2.0\"");
        using var output = new StringWriter();
        var session = new McpSession("9");

        _ = await Assert.ThrowsAsync<InvalidDataException>(() => McpStdio.RunAsync(input, output, session, TestContext.Current.CancellationToken));
        Assert.Equal("", output.ToString());
    }
}
