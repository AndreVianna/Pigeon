using System.Diagnostics;
using System.Text.RegularExpressions;

using Reqnroll;

using Xunit;

namespace Pigeon.IntegrationTests.Steps;

// Two real `pigeon start` services on loopback with their own databases, ports and identity
// folders (never the user's certificate store), driven only through the command line.
[Binding]
[Scope(Tag = "peers")]
public sealed partial class PeerSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-peers-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, (string Port, string PeerPort)> services = [];

    private async Task<(int ExitCode, string Output, string Error)> Run(string who, string? input, params string[] arguments) {
        var info = new ProcessStartInfo(TestTools.Pigeon) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        if (who != "nobody") {
            info.Environment["PIGEON_PORT"] = services[who].Port;
            info.Environment["PIGEON_DATABASE"] = Path.Combine(directory, who + ".db");
            info.Environment["PIGEON_IDENTITY_DIR"] = Path.Combine(directory, who + "-identity");
        }

        using var process = Process.Start(info)!;
        if (input is not null)
            await process.StandardInput.WriteLineAsync(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            return (process.ExitCode, await output, await error);
        }
        finally {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private async Task<string> Ok(string who, params string[] arguments) => await OkWith(who, null, arguments);

    private async Task<string> OkWith(string who, string? input, params string[] arguments) {
        var (code, output, error) = await Run(who, input, arguments);
        Assert.True(code == 0, $"{who}: pigeon {string.Join(' ', arguments)} exited {code}: {error}{output}");
        return output;
    }

    // Delivery runs every two seconds; waits for a condition instead of a fixed time.
    private static async Task Eventually(Func<Task<bool>> condition) {
        for (var i = 0; i < 40; i++) {
            if (await condition())
                return;
            await Task.Delay(500);
        }

        Assert.Fail("The peers did not get there in 20 seconds.");
    }

    [GeneratedRegex("Join code: ([0-9]{6})")]
    private static partial Regex JoinCodeLine();
    [GeneratedRegex(@"Request ([0-9a-f]{12})")]
    private static partial Regex RequestLine();

    private string? brunoRequest, code;

    [Given("two isolated services, each listening for other machines")]
    public async Task TwoServices() {
        Directory.CreateDirectory(directory);
        foreach (var (who, name) in new[] { ("ana", "ana-laptop"), ("bruno", "bruno-desktop") }) {
            services[who] = (TestPorts.FreePort(), TestPorts.FreePort());
            Assert.Contains("Pigeon service started.", await Ok(who, "start"));
            Assert.Contains($"\"{name}\"", await Ok(who, "peer", "name", name));
            Assert.Contains($"at 127.0.0.1:{services[who].PeerPort}", await Ok(who, "peer", "listen", "127.0.0.1:" + services[who].PeerPort));
        }
    }

    // The ID at the start of the `pigeon requests` line that mentions the text.
    private async Task<string> RequestId(string who, string mentioning) {
        string? id = null;
        await Eventually(async () => (id = (await Ok(who, "requests")).Split('\n').FirstOrDefault(l => l.Contains(mentioning, StringComparison.Ordinal))?.Split(' ')[0]) is { Length: 12 });
        return id!;
    }

    private async Task AskByAddress(string topic) {
        Assert.Contains("Joined " + topic + ". Waiting for a partner.", await Ok("ana", "join", topic, "as", "ana-session", "using", "claude-code", "--remote"));
        var asked = await Ok("bruno", "join", $"{topic}@127.0.0.1:{services["ana"].PeerPort}", "as", "bruno-session", "using", "codex");
        Assert.Contains("a machine you have not joined before", asked);
        brunoRequest = RequestLine().Match(asked).Groups[1].Value;
        Assert.Contains($"Contact it: pigeon allow {brunoRequest}", await Ok("bruno", "requests"));
        Assert.Contains("Contacting it.", await Ok("bruno", "allow", brunoRequest));
        await Eventually(async () => (await Run("bruno", null, "requests", "--show", brunoRequest)) is { ExitCode: 0 } shown && (code = JoinCodeLine().Match(shown.Output).Groups[1].Value).Length == 6);
    }

    [When("Ana opens a topic to other machines and Bruno asks to join it by address")]
    public async Task Ask() => await AskByAddress("review");

    [When("Bruno confirms the contact, reads his join code, and Ana allows the request with it")]
    public async Task Allow() {
        var id = await RequestId("ana", "wants to join review");
        Assert.Contains("A new machine: allow with the code its person gives you", await Ok("ana", "requests"));
        Assert.Contains("Allowed.", await Ok("ana", "allow", id, code!));
        await Eventually(async () => (await Ok("bruno", "topics")) is var t && t.Contains("review@ana-laptop:") && t.Contains("claude-code @ana-laptop") && t.Contains("codex bruno-session"));
    }

    [Then("a message from each side reaches the other, from the machine and never a session ID")]
    public async Task Exchange() {
        Assert.StartsWith("On its way to the other machine", await Ok("ana", "send", "from", "ana-session", "to", "review", "hello from Ana"));
        string received = "";
        await Eventually(async () => (received = await Ok("bruno", "receive", "for", "bruno-session", "from", "review@ana-laptop")).Contains("hello from Ana"));
        Assert.Contains("claude-code @ana-laptop: hello from Ana", received);
        Assert.DoesNotContain("ana-session", received);
        _ = await Ok("bruno", "send", "from", "bruno-session", "to", "review@ana-laptop", "hi Ana");
        await Eventually(async () => (received = await Ok("ana", "receive", "for", "ana-session", "from", "review")).Contains("hi Ana"));
        Assert.Contains("codex @bruno-desktop: hi Ana", received);
        Assert.DoesNotContain("bruno-session", received);
    }

    [Then("each side lists the other as a known machine with nothing left to send")]
    public async Task Listed() {
        Assert.Contains("bruno-desktop at 127.0.0.1:" + services["bruno"].PeerPort, await Ok("ana", "peer", "list"));
        Assert.Contains("waiting to send: 0", await Ok("ana", "peer", "list"));
        Assert.Contains("ana-laptop at 127.0.0.1:" + services["ana"].PeerPort, await Ok("bruno", "peer", "list"));
    }

    [Then("a second topic from the known machine is allowed without a code")]
    public async Task KnownMachine() {
        _ = await Ok("ana", "join", "second", "as", "ana-2", "using", "claude-code", "--remote");
        // Found in the two-machine test: topic lists did not say which topics other machines can join.
        Assert.Contains("second: claude-code ana-2 (waiting for a partner) | 0 waiting | open to other machines", await Ok("ana", "topics"));
        Assert.Contains("second@ana-laptop", await Ok("bruno", "topics", "on", "ana-laptop"));
        Assert.Contains("Request submitted to ana-laptop", await Ok("bruno", "join", "second@ana-laptop", "as", "bruno-2", "using", "codex"));
        var id = await RequestId("ana", "wants to join second");
        Assert.Contains($"pigeon allow {id}   or", await Ok("ana", "requests"));
        Assert.Contains("Allowed.", await Ok("ana", "allow", id));
        await Eventually(async () => (await Ok("bruno", "topics")).Contains("second@ana-laptop"));
    }

    // Found in the two-machine test on 2026-11-07: the removed side kept its topic and retried for a
    // day. From 0.6.0-alpha.8 an agent forgets a machine when its person asks, as `pigeon peer remove` does.
    [Then("when Ana's agent forgets Bruno's machine, Bruno's side ends the topics and forgets Ana's machine too")]
    public async Task Removed() {
        var agent = new Pigeon.Service.McpSession(services["ana"].Port, "claude-code");
        _ = await agent.HandleAsync("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
        _ = await agent.HandleAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        async Task<(bool Error, string Text)> Call(string tool, object args) {
            using var reply = System.Text.Json.JsonDocument.Parse((await agent.HandleAsync(System.Text.Json.JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } })))!);
            var result = reply.RootElement.GetProperty("result");
            return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
        }

        Assert.False((await Call("pigeon_join", new { topic = "review", sessionId = "ana-session" })).Error);
        Assert.Contains("not both", (await Call("pigeon_leave", new { topic = "review", machine = "bruno-desktop" })).Text);
        Assert.Contains("No known machine is named nobody-pc", (await Call("pigeon_leave", new { machine = "nobody-pc" })).Text);
        var (error, text) = await Call("pigeon_leave", new { machine = "bruno-desktop" });
        Assert.False(error, text);
        // Found in the dialog host test (D7): "ended on both machines" made an agent think the topics were gone.
        Assert.Contains("Forgot bruno-desktop, and it was told. You no longer share any topic with it", text);
        await Eventually(async () => (await Ok("bruno", "peer", "list")).Contains("Known machines: 0"));
        Assert.DoesNotContain("@ana-laptop", await Ok("bruno", "topics"));
        Assert.Contains("Known machines: 0", await Ok("ana", "peer", "list"));
    }

    [Then("a wrong code is refused, a denied request joins nothing, and Antigravity may ask too")]
    public async Task Refusals() {
        await AskByAddress("review");
        var id = await RequestId("ana", "wants to join review");
        var (exit, output, _) = await Run("ana", null, "allow", id, code == "000000" ? "111111" : "000000");
        Assert.Equal(1, exit);
        Assert.Contains("does not match", output);
        Assert.Contains("Refused.", await Ok("ana", "deny", id));
        await Eventually(async () => !(await Ok("bruno", "requests")).Contains(brunoRequest!));
        Assert.DoesNotContain("review@", await Ok("bruno", "topics"));
        Assert.DoesNotContain("bruno", await Ok("ana", "peer", "list"));

        // Until 0.6.0-alpha.6 Antigravity was limited to local topics.
        Assert.Contains("a machine you have not joined before", await Ok("bruno", "join", $"review@127.0.0.1:{services["ana"].PeerPort}", "as", "agy-1", "using", "antigravity"));
    }

    [Then("invalid peer, request and join commands exit with a usage error")]
    public async Task Usage() {
        foreach (var arguments in new[] { new[] { "peer" }, ["peer", "dance"], ["peer", "invite"], ["peer", "remove"], ["requests", "--show"], ["allow"], ["deny"], ["deny", "a", "b"],
            ["join", "x", "as", "s", "using", "t", "--bogus"], ["join", "x", "as", "s", "using", "t", "--remote", "--remote"], ["topics", "on"], ["topic", "share", "x", "with", "y"] })
            Assert.Equal(2, (await Run("nobody", null, arguments)).ExitCode);
    }

    [AfterScenario]
    public async Task Cleanup() {
        foreach (var who in services.Keys)
            _ = await Run(who, null, "stop");
        for (var i = 0; i < 20 && Directory.Exists(directory); i++) {
            try {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) {
                await Task.Delay(200);
            }
        }
    }
}
