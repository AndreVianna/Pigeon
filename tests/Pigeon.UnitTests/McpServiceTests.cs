using Pigeon.Service;

using Xunit;

namespace Pigeon.UnitTests;

// What a tool call says when the service cannot answer it. Nothing starts the service on its own.
public sealed class McpServiceTests {
    private const string Session = "8d77a0ec-d9c8-46b4-9948-7831ad4609ef";

    // What LocalHttpClient throws when nothing listens on the port.
    private static IOException NothingListening() => new("Pigeon service is unavailable", new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

    private static async Task<string> JoinWhile(string port, Exception failure) {
        var service = new FakeService { Unreachable = failure };
        var session = await McpHost.Ready(new McpSession(port, "codex", service: service.Call));
        var (error, text) = await McpHost.Call(session, "pigeon_join", new { topic = "review", sessionId = Session });
        Assert.True(error);
        return text;
    }

    // Found in the 0.7.0-beta.3 test: with the service stopped, the tools said only that it was unavailable.
    [Fact]
    public async Task A_service_that_is_not_running_is_reported_with_how_to_start_it()
        => Assert.Equal("The Pigeon service is not running on 127.0.0.1:29372. Start it with 'pigeon start' in a terminal, then try again.", await JoinWhile("29372", NothingListening()));

    [Fact]
    public async Task A_service_expected_on_another_port_names_the_port_to_start_it_with()
        => Assert.Equal("The Pigeon service is not running on 127.0.0.1:29482. Start it with 'pigeon start' in a terminal with PIGEON_PORT set to 29482, then try again.", await JoinWhile("29482", NothingListening()));

    [Fact]
    public async Task A_slow_service_or_locked_storage_is_reported_as_not_answering_in_time() {
        const string slow = "The Pigeon service did not answer in time: it may be busy, or its storage locked for a moment. Try again shortly.";
        Assert.Equal(slow, await JoinWhile("29372", new TaskCanceledException("timeout", new TimeoutException())));
        Assert.Equal(slow, await JoinWhile("29372", new IOException("Pigeon storage is temporarily unavailable")));
    }
}
