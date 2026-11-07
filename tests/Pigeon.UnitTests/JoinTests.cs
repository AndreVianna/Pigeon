using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

using Pigeon.Service;
using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// Two services on loopback, each with its own identity, peer host and listener, as two machines
// would be. The tests answer the prompts the way the MCP servers pass on a person's answers.
public sealed partial class JoinTests : IAsyncLifetime {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-join-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource stop = new();
    private Broker ana = null!, bruno = null!;
    private PeerHost anaHost = null!, brunoHost = null!;
    private Task anaRun = Task.CompletedTask, brunoRun = Task.CompletedTask;
    private int anaPort;
    private static readonly string Bridge = Guid.NewGuid().ToString("N");

    private static int FreePort() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask InitializeAsync() {
        Directory.CreateDirectory(directory);
        ana = new Broker(Path.Combine(directory, "ana.db"));
        bruno = new Broker(Path.Combine(directory, "bruno.db"));
        anaHost = new PeerHost(ana, new FileIdentityStore(Path.Combine(directory, "ana")));
        brunoHost = new PeerHost(bruno, new FileIdentityStore(Path.Combine(directory, "bruno")));
        anaRun = anaHost.RunAsync(stop.Token);
        brunoRun = brunoHost.RunAsync(stop.Token);
        anaPort = FreePort();
        foreach (var (host, name, port) in new[] { (anaHost, "ana-laptop", anaPort), (brunoHost, "bruno-desktop", FreePort()) }) {
            Assert.True((await host.ExecuteAsync("peer_name", Args(new { name }))).Ok);
            var listen = await host.ExecuteAsync("peer_listen", Args(new { address = $"127.0.0.1:{port}" }));
            Assert.True(listen.Ok, listen.Error);
        }
    }

    private static JsonElement Args(object args) => JsonSerializer.SerializeToElement(args);

    private static JsonElement Call(Broker broker, string operation, object args) {
        var result = broker.Execute(operation, Args(args));
        Assert.True(result.Ok, result.Error);
        return JsonSerializer.SerializeToElement(result.Value);
    }

    private static string? Fail(Broker broker, string operation, object args) {
        var result = broker.Execute(operation, Args(args));
        Assert.False(result.Ok);
        return result.Error;
    }

    private static async Task<T> Eventually<T>(Func<T?> probe) where T : class {
        for (var i = 0; i < 200; i++) {
            if (probe() is { } value)
                return value;
            await Task.Delay(100);
        }

        throw new TimeoutException("The condition never held.");
    }

    // The next prompt for a session whose ID ends with kind, as an MCP server would fetch it.
    private static async Task<JsonElement> Prompt(Broker broker, string session, string kind) => (await Eventually(() => {
        var prompts = Call(broker, "prompts_next", new { bridgeId = Bridge, sessions = new[] { session } }).EnumerateArray().ToArray();
        return prompts.Where(p => p.GetProperty("id").GetString()!.EndsWith(":" + kind, StringComparison.Ordinal)).Select(p => (object)p).FirstOrDefault();
    }) is JsonElement prompt ? prompt : default);

    private static string Answer(Broker broker, JsonElement prompt, string action, string? code = null)
        => Call(broker, "prompt_answer", new { id = prompt.GetProperty("id").GetString(), action, code }).GetProperty("result").GetString()!;

    [GeneratedRegex("[0-9]{6}")]
    private static partial Regex Digits();

    private async Task<string[]> Bodies(Broker broker, string session, string topic) => await Eventually(() => {
        var taken = broker.Execute("take", Args(new { sessionId = session, topic }));
        if (!taken.Ok)
            return null;
        var bodies = JsonSerializer.SerializeToElement(taken.Value).EnumerateArray().Select(d => d.GetProperty("Body").GetString()!).ToArray();
        return bodies.Length == 0 ? null : bodies;
    });

    // The first join between two machines: the requesting person confirms the contact and is shown
    // the code; the listing person enters it. Afterwards the machines know each other.
    private async Task FirstJoin() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "review", remote = true });
        var pending = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-session", topic = $"review@127.0.0.1:{anaPort}" });
        Assert.True(pending.GetProperty("pending").GetBoolean());
        Assert.Contains("Do not call pigeon_join again", pending.GetProperty("text").GetString());
        Assert.Equal("contacting", Answer(bruno, await Prompt(bruno, "bruno-session", "confirm"), "accept"));
        var shown = await Prompt(bruno, "bruno-session", "show");
        var code = Digits().Match(shown.GetProperty("message").GetString()!).Value;
        Assert.Equal(code, Call(bruno, "request_show", new { id = pending.GetProperty("request").GetString() }).GetProperty("code").GetString());

        var asked = await Prompt(ana, "ana-session", "code");
        Assert.Contains("a new machine, bruno-desktop", asked.GetProperty("message").GetString());
        Assert.True(asked.GetProperty("code").GetBoolean());
        Assert.Equal("wrong code", Answer(ana, asked, "accept", code == "000000" ? "111111" : "000000"));
        Assert.Equal("allowed", Answer(ana, asked, "accept", code));
        _ = await Eventually(() => Call(bruno, "topics", new { sessionId = "bruno-session" }).EnumerateArray().Any(t => t.GetProperty("Topic").GetString() == "review@ana-laptop") ? "" : null);
    }

    [Fact]
    public async Task A_first_join_takes_the_code_from_one_person_to_the_other_and_then_messages_flow_both_ways() {
        await FirstJoin();
        Assert.Contains(await Bodies(bruno, "bruno-session", "review@ana-laptop"), b => b.Contains("You joined review@ana-laptop, with claude-code @ana-laptop"));
        Assert.Contains(await Bodies(ana, "ana-session", "review"), b => b.Contains("codex @bruno-desktop joined review"));

        _ = Call(ana, "send", new { sessionId = "ana-session", topic = "review", message = "hello from Ana" });
        Assert.Equal(["hello from Ana"], await Bodies(bruno, "bruno-session", "review@ana-laptop"));
        _ = Call(bruno, "send", new { sessionId = "bruno-session", topic = "review@ana-laptop", message = "hi Ana" });
        Assert.Equal(["hi Ana"], await Bodies(ana, "ana-session", "review"));

        // Bruno leaving ends the topic on both machines, and Ana's topic is open again.
        _ = Call(bruno, "leave", new { sessionId = "bruno-session", topic = "review@ana-laptop" });
        Assert.Contains(await Bodies(ana, "ana-session", "review"), b => b.Contains("@bruno-desktop left review"));
    }

    [Fact]
    public async Task A_known_machine_is_allowed_or_denied_without_a_code() {
        await FirstJoin();
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-2", topic = "second", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-2", topic = "second@ana-laptop" });
        var asked = await Prompt(ana, "ana-2", "allow");
        Assert.False(asked.GetProperty("code").GetBoolean());
        Assert.Contains("bruno-desktop wants to join your topic \"second\" with codex", asked.GetProperty("message").GetString());
        Assert.Equal("allowed", Answer(ana, asked, "accept"));
        Assert.Contains(await Bodies(bruno, "bruno-2", "second@ana-laptop"), b => b.Contains("You joined second@ana-laptop"));

        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-3", topic = "third", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-3", topic = "third@ana-laptop" });
        Assert.Equal("denied", Answer(ana, await Prompt(ana, "ana-3", "allow"), "cancel"));
        // The refusal reaches the requesting session through its wake, although no topic was created.
        var claim = await Eventually(() => Call(bruno, "wake_claim", new { sessionId = "bruno-3", mode = "codex-queue" }) is { ValueKind: JsonValueKind.Object } c ? (object)c : null);
        var batch = Call(bruno, "wake_receive", new { sessionId = "bruno-3", generation = ((JsonElement)claim).GetProperty("Generation").GetString() });
        Assert.Contains("did not allow joining third", batch.GetProperty("Messages")[0].GetProperty("Body").GetString());
    }

    [Fact]
    public async Task An_answer_counts_only_while_its_request_is_open() {
        await FirstJoin();
        // The listing prompt was answered; answering it again, as a host replaying it would, changes nothing.
        Assert.StartsWith("ignored", Call(ana, "prompt_answer", new { id = "nonexistent:code", action = "accept", code = "123456" }).GetProperty("result").GetString());
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-2", topic = "second", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-2", topic = "second@ana-laptop" });
        var asked = await Prompt(ana, "ana-2", "allow");
        Assert.Equal("denied", Answer(ana, asked, "decline"));
        Assert.StartsWith("ignored", Answer(ana, asked, "accept"));
    }

    [Fact]
    public async Task Nothing_reaches_a_topic_that_is_not_open_and_Antigravity_stays_local() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "open", remote = true });
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "private" });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-session", topic = $"private@127.0.0.1:{anaPort}" });
        _ = Answer(bruno, await Prompt(bruno, "bruno-session", "confirm"), "accept");
        // The request looks the same from outside, but nobody here is asked.
        _ = await Prompt(bruno, "bruno-session", "show");
        await Task.Delay(1500);
        Assert.Empty(Call(ana, "prompts_next", new { bridgeId = Bridge, sessions = new[] { "ana-session" } }).EnumerateArray());
        Assert.Empty(Call(ana, "requests", new { }).GetProperty("incoming").EnumerateArray());

        // Antigravity's prompts appear in Pigeon's windows, so it is no longer limited to local topics.
        Assert.True(Call(bruno, "join", new { tool = "antigravity", sessionId = "agy", topic = $"open@127.0.0.1:{anaPort}" }).GetProperty("pending").GetBoolean());
        _ = Call(ana, "join", new { tool = "antigravity", sessionId = "agy", topic = "mine", remote = true });
    }

    [Fact]
    public async Task A_local_topic_that_requires_approval_admits_a_joiner_only_with_its_code() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "opener", topic = "guarded", approval = true });
        var pending = Call(ana, "join", new { tool = "codex", sessionId = "joiner", topic = "guarded" });
        var id = pending.GetProperty("request").GetString()!;
        var code = Digits().Match((await Prompt(ana, "joiner", "show")).GetProperty("message").GetString()!).Value;
        Assert.Equal(code, Call(ana, "request_show", new { id }).GetProperty("code").GetString());
        Assert.Contains("needs the join code", Fail(ana, "request_allow", new { id }));
        Assert.Equal("wrong code", Call(ana, "request_allow", new { id, code = code == "000000" ? "111111" : "000000" }).GetProperty("result").GetString());
        Assert.Equal("allowed", Call(ana, "request_allow", new { id, code }).GetProperty("result").GetString());
        var topic = Call(ana, "topics", new { sessionId = "joiner" })[0];
        Assert.Equal(2, topic.GetProperty("Members").GetArrayLength());
        Assert.Contains(await Bodies(ana, "opener", "guarded"), b => b.Contains("codex joiner joined guarded"));
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() {
        await stop.CancelAsync();
        await Task.WhenAll(anaRun, brunoRun);
        anaHost.Dispose();
        brunoHost.Dispose();
        ana.Dispose();
        bruno.Dispose();
        stop.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
