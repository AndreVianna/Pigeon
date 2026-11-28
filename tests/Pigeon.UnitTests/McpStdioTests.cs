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

    private const string Session = "c1122145-145c-4af1-b02b-c2731e423fed";

    // A Claude connection whose session the service restores as soon as the host is ready.
    private static (FakeService Service, McpSession Session, Task Bound) Restoring() {
        var service = new FakeService().Answer("session_restore", _ => new { SessionId = Session });
        var bound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Called += (operation, args) => {
            if (operation == "bridge_heartbeat" && args.GetProperty("sessionId").ValueKind == System.Text.Json.JsonValueKind.String)
                bound.TrySetResult();
        };
        return (service, new McpSession("1", "claude-code", Session, service: service.Call), bound.Task);
    }

    // Found in the 0.7.0-beta.3 test on Ubuntu: Claude Code ends its server with SIGINT then SIGTERM,
    // which cancel the run while the console read blocks its thread. The run must still stop and
    // tell the service which session's server closed.
    [Fact]
    public async Task A_cancelled_token_stops_a_blocked_read_and_reports_the_close() {
        var (service, session, bound) = Restoring();
        using var input = new HostInput();
        input.Send(McpHost.Initialize);
        input.Send(McpHost.Initialized);
        using var stop = new CancellationTokenSource();
        var running = McpStdio.RunAsync(input, TextWriter.Null, session, stop.Token, delay: McpHost.Quickly);
        await bound.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var close = Assert.Single(service.CallsTo("bridge_disconnect"));
        Assert.Equal(Session, close.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task End_of_input_reports_the_close_with_the_session() {
        var (service, session, bound) = Restoring();
        using var input = new HostInput();
        input.Send(McpHost.Initialize);
        input.Send(McpHost.Initialized);
        var running = McpStdio.RunAsync(input, TextWriter.Null, session, TestContext.Current.CancellationToken, delay: McpHost.Quickly);
        await bound.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        input.End();
        await running.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var close = Assert.Single(service.CallsTo("bridge_disconnect"));
        Assert.Equal(Session, close.GetProperty("sessionId").GetString());
    }
}
