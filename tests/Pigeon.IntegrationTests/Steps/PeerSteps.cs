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
    private string? invitation;

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

    [GeneratedRegex("Check code: ([0-9]{6})")]
    private static partial Regex CheckCode();

    [Given("two isolated services, each named and listening for peers")]
    public async Task TwoServices() {
        Directory.CreateDirectory(directory);
        foreach (var (who, name) in new[] { ("ana", "ana-laptop"), ("bruno", "bruno-desktop") }) {
            services[who] = (TestPorts.FreePort(), TestPorts.FreePort());
            Assert.Contains("Pigeon service started.", await Ok(who, "start"));
            Assert.Contains($"\"{name}\"", await Ok(who, "peer", "name", name));
            Assert.Contains("Listening for paired peers on 127.0.0.1:" + services[who].PeerPort, await Ok(who, "peer", "listen", "127.0.0.1:" + services[who].PeerPort));
        }
    }

    [When("Ana invites Bruno, Bruno accepts and confirms the check code, and Ana confirms the same code")]
    public async Task Pair() {
        invitation = (await Ok("ana", "peer", "invite")).Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("pigeon1.", StringComparison.Ordinal));
        var accepted = await OkWith("bruno", "y", "peer", "accept", invitation);
        Assert.Contains("its certificate matches the invitation", accepted);
        var check = CheckCode().Match(accepted).Groups[1].Value;
        Assert.Contains("accepted your invitation", await Ok("ana", "peer", "list"));
        Assert.Contains("Paired with \"bruno-desktop\"", await Ok("ana", "peer", "confirm", check));
    }

    [When("Ana shares a topic with Bruno and both sessions join it")]
    public async Task ShareAndJoin() {
        _ = await Ok("ana", "join", "review", "as", "ana-session", "using", "claude-code");
        Assert.Contains("Shared \"review\" with bruno-desktop", await Ok("ana", "topic", "share", "review", "with", "bruno-desktop"));
        await Eventually(async () => (await Ok("bruno", "peer", "list")).Contains("review@ana-laptop shared by ana-laptop"));
        Assert.Contains("@ana-laptop", await Ok("bruno", "join", "review@ana-laptop", "as", "bruno-session", "using", "codex"));
    }

    [Then("a message from each side reaches the other, from the peer and never a session ID")]
    public async Task Exchange() {
        Assert.StartsWith("Queued for the other machine", await Ok("ana", "send", "from", "ana-session", "to", "review", "hello from Ana"));
        string received = "";
        await Eventually(async () => (received = await Ok("bruno", "receive", "for", "bruno-session", "from", "review@ana-laptop")).Contains("hello from Ana"));
        Assert.Contains("claude-code @ana-laptop: hello from Ana", received);
        Assert.DoesNotContain("ana-session", received);
        _ = await Ok("bruno", "send", "from", "bruno-session", "to", "review@ana-laptop", "hi Ana");
        await Eventually(async () => (received = await Ok("ana", "receive", "for", "ana-session", "from", "review")).Contains("hi Ana"));
        Assert.Contains("codex @bruno-desktop: hi Ana", received);
        Assert.DoesNotContain("bruno-session", received);
    }

    [Then("each side lists the other as a peer with nothing left to send")]
    public async Task Listed() {
        Assert.Contains("bruno-desktop at 127.0.0.1:" + services["bruno"].PeerPort, await Ok("ana", "peer", "list"));
        Assert.Contains("waiting to send: 0", await Ok("ana", "peer", "list"));
        Assert.Contains("ana-laptop at 127.0.0.1:" + services["ana"].PeerPort, await Ok("bruno", "peer", "list"));
    }

    [Then("joining a topic that was not shared fails, a wrong check code pairs nothing, and an invitation works once")]
    public async Task Refusals() {
        var (code, _, error) = await Run("bruno", null, "join", "secret@ana-laptop", "as", "bruno-session", "using", "codex");
        Assert.Equal(1, code);
        Assert.Contains("has not been shared with this machine", error);
        invitation = (await Ok("ana", "peer", "invite")).Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("pigeon1.", StringComparison.Ordinal));
        // Bruno answers no: nothing is stored on his side.
        Assert.Equal(1, (await Run("bruno", "n", "peer", "accept", invitation)).ExitCode);
        Assert.Contains("Peers: 0", await Ok("bruno", "peer", "list"));
        // Ana types a code that is not the one Bruno saw: nothing is paired.
        (code, _, error) = await Run("ana", null, "peer", "confirm", "000000");
        Assert.Equal(1, code);
        Assert.Contains("No pairing waits for that check code", error);
        Assert.Contains("Peers: 0", await Ok("ana", "peer", "list"));
        // The invitation was used by the first acceptance.
        (code, _, error) = await Run("bruno", "y", "peer", "accept", invitation);
        Assert.Equal(1, code);
        Assert.Contains("Could not pair", error);
    }

    [Then("invalid peer and topic commands exit with a usage error")]
    public async Task Usage() {
        foreach (var arguments in new[] { new[] { "peer" }, ["peer", "dance"], ["peer", "accept"], ["peer", "confirm", "1", "to", "x"], ["topic", "share", "x"], ["topic", "share", "x", "to", "y"], ["topic", "unshare"] })
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
