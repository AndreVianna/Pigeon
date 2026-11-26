using System.Text.Json;
using System.Text.RegularExpressions;

using Pigeon.Service;
using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// Two services, each with its own database, identity and peer host, as two machines would be,
// connected in memory and sharing a clock that only the tests move. The tests answer the prompts the
// way the MCP servers pass on a person's answers.
public sealed partial class JoinTests(JoinTests.Identities identities) : IAsyncLifetime, IClassFixture<JoinTests.Identities> {
    // Each machine's identity, made once for the class, since making one takes longer than a test;
    // each test's peer host loads it from its folder.
    public sealed class Identities : IDisposable {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-join-identities-" + Guid.NewGuid().ToString("N"));

        public Identities() {
            foreach (var who in new[] { "ana", "bruno" })
                ServiceIdentity.LoadOrCreate(Store(who)).Dispose();
        }

        public IIdentityStore Store(string who) => new FileIdentityStore(Path.Combine(directory, who));

        public void Dispose() {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private const string AnaAt = "192.0.2.1:29374", BrunoAt = "192.0.2.2:29374";
    private static readonly string Bridge = Guid.NewGuid().ToString("N");
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-join-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryPeerNetwork network = new();
    private long now = 1_790_000_000_000;
    private Broker ana = null!, bruno = null!;
    private PeerHost anaHost = null!, brunoHost = null!;

    public async ValueTask InitializeAsync() {
        Directory.CreateDirectory(directory);
        (ana, anaHost) = await Machine("ana", "ana-laptop", AnaAt);
        (bruno, brunoHost) = await Machine("bruno", "bruno-desktop", BrunoAt);
    }

    // A named machine listening at address: the setting `pigeon peer listen` records, and its
    // listener's protocol on the in-memory network instead of a socket.
    private async Task<(Broker, PeerHost)> Machine(string who, string name, string address) {
        var broker = new Broker(Path.Combine(directory, who + ".db"), () => now);
        var host = new PeerHost(broker, identities.Store(who), network.From(address));
        Assert.True((await host.ExecuteAsync("peer_name", Args(new { name }))).Ok);
        broker.SetPeerSetting("listen", address);
        network.Listen(address, host.Server());
        return (broker, host);
    }

    // Two seconds pass on both machines: each contacts the machines it was asked to, asks about its
    // open requests and delivers what is due, as the peer host does on its own every second.
    private async Task Pass() {
        now += 2000;
        await anaHost.PassAsync(TestContext.Current.CancellationToken);
        await brunoHost.PassAsync(TestContext.Current.CancellationToken);
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

    private static JsonElement[] Prompts(Broker broker, string session)
        => [.. Call(broker, "prompts_next", new { bridgeId = Bridge, sessions = new[] { session } }).EnumerateArray()];

    // The prompt for a session whose ID ends with kind, as an MCP server would fetch it.
    private static JsonElement Prompt(Broker broker, string session, string kind)
        => Assert.Single(Prompts(broker, session), p => p.GetProperty("id").GetString()!.EndsWith(":" + kind, StringComparison.Ordinal));

    private static string Answer(Broker broker, JsonElement prompt, string action, string? code = null)
        => Call(broker, "prompt_answer", new { id = prompt.GetProperty("id").GetString(), action, code }).GetProperty("result").GetString()!;

    [GeneratedRegex("[0-9]{6}")]
    private static partial Regex Digits();

    private static string[] Bodies(Broker broker, string session, string topic)
        => [.. Call(broker, "take", new { sessionId = session, topic }).EnumerateArray().Select(d => d.GetProperty("Body").GetString()!)];

    // The first join between two machines: the requesting person confirms the contact and is shown
    // the code; the listing person enters it. Afterwards the machines know each other.
    private async Task FirstJoin() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "review", remote = true });
        var pending = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-session", topic = $"review@{AnaAt}" });
        Assert.True(pending.GetProperty("pending").GetBoolean());
        Assert.Contains("Do not call pigeon_join again", pending.GetProperty("text").GetString());
        Assert.Equal("contacting", Answer(bruno, Prompt(bruno, "bruno-session", "confirm"), "accept"));
        await Pass();
        var shown = Prompt(bruno, "bruno-session", "show");
        var code = Digits().Match(shown.GetProperty("message").GetString()!).Value;
        Assert.Equal(code, Call(bruno, "request_show", new { id = pending.GetProperty("request").GetString() }).GetProperty("code").GetString());

        var asked = Prompt(ana, "ana-session", "code");
        Assert.Contains("a new machine, bruno-desktop", asked.GetProperty("message").GetString());
        Assert.True(asked.GetProperty("code").GetBoolean());
        Assert.Equal("wrong code", Answer(ana, asked, "accept", code == "000000" ? "111111" : "000000"));
        Assert.Equal("allowed", Answer(ana, asked, "accept", code));
        await Pass();
        Assert.Contains(Call(bruno, "topics", new { sessionId = "bruno-session" }).EnumerateArray(), t => t.GetProperty("Topic").GetString() == "review@ana-laptop");
    }

    [Fact]
    public async Task A_known_machine_is_allowed_or_denied_without_a_code() {
        await FirstJoin();
        Assert.Contains(Bodies(bruno, "bruno-session", "review@ana-laptop"), b => b.Contains("You joined review@ana-laptop, with claude-code @ana-laptop"));
        Assert.Contains(Bodies(ana, "ana-session", "review"), b => b.Contains("codex @bruno-desktop joined review"));
        // Bruno leaving ends the topic on both machines.
        _ = Call(bruno, "leave", new { sessionId = "bruno-session", topic = "review@ana-laptop" });
        await Pass();
        Assert.Contains(Bodies(ana, "ana-session", "review"), b => b.Contains("@bruno-desktop left review"));

        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-2", topic = "second", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-2", topic = "second@ana-laptop" });
        await Pass();
        var asked = Prompt(ana, "ana-2", "allow");
        Assert.False(asked.GetProperty("code").GetBoolean());
        Assert.Contains("bruno-desktop wants to join your topic \"second\" with codex", asked.GetProperty("message").GetString());
        Assert.Equal("allowed", Answer(ana, asked, "accept"));
        await Pass();
        Assert.Contains(Bodies(bruno, "bruno-2", "second@ana-laptop"), b => b.Contains("You joined second@ana-laptop"));

        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-3", topic = "third", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-3", topic = "third@ana-laptop" });
        await Pass();
        var third = Prompt(ana, "ana-3", "allow");
        Assert.Equal("denied", Answer(ana, third, "cancel"));
        // An answer counts only while its request is open: a replayed or unknown one changes nothing.
        Assert.StartsWith("ignored", Answer(ana, third, "accept"));
        Assert.StartsWith("ignored", Call(ana, "prompt_answer", new { id = "nonexistent:code", action = "accept", code = "123456" }).GetProperty("result").GetString());
        // The refusal reaches the requesting session through its wake, although no topic was created.
        await Pass();
        var claim = Call(bruno, "wake_claim", new { sessionId = "bruno-3", mode = "codex-queue" });
        Assert.Equal(JsonValueKind.Object, claim.ValueKind);
        var batch = Call(bruno, "wake_receive", new { sessionId = "bruno-3", generation = claim.GetProperty("Generation").GetString() });
        Assert.Contains("did not allow joining third", batch.GetProperty("Messages")[0].GetProperty("Body").GetString());
    }

    [Fact]
    public async Task Nothing_reaches_a_topic_that_is_not_open_and_Antigravity_may_use_other_machines() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "open", remote = true });
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "private" });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-session", topic = $"private@{AnaAt}" });
        _ = Answer(bruno, Prompt(bruno, "bruno-session", "confirm"), "accept");
        await Pass();
        // The request looks the same from outside, but nobody here is asked, however long it waits.
        _ = Prompt(bruno, "bruno-session", "show");
        await Pass();
        Assert.Empty(Prompts(ana, "ana-session"));
        Assert.Empty(Call(ana, "requests", new { }).GetProperty("incoming").EnumerateArray());

        // Antigravity's prompts appear in Pigeon's windows, so it is no longer limited to local topics.
        Assert.True(Call(bruno, "join", new { tool = "antigravity", sessionId = "agy", topic = $"open@{AnaAt}" }).GetProperty("pending").GetBoolean());
        _ = Call(ana, "join", new { tool = "antigravity", sessionId = "agy", topic = "mine", remote = true });
    }

    [Fact]
    public void A_local_topic_that_requires_approval_admits_a_joiner_only_with_its_code() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "opener", topic = "guarded", approval = true });
        var pending = Call(ana, "join", new { tool = "codex", sessionId = "joiner", topic = "guarded" });
        var id = pending.GetProperty("request").GetString()!;
        var code = Digits().Match(Prompt(ana, "joiner", "show").GetProperty("message").GetString()!).Value;
        Assert.Equal(code, Call(ana, "request_show", new { id }).GetProperty("code").GetString());
        Assert.Contains("needs the join code", Fail(ana, "request_allow", new { id }));
        Assert.Equal("wrong code", Call(ana, "request_allow", new { id, code = code == "000000" ? "111111" : "000000" }).GetProperty("result").GetString());
        Assert.Equal("allowed", Call(ana, "request_allow", new { id, code }).GetProperty("result").GetString());
        var topic = Call(ana, "topics", new { sessionId = "joiner" })[0];
        Assert.Equal(2, topic.GetProperty("Members").GetArrayLength());
        Assert.Contains(Bodies(ana, "opener", "guarded"), b => b.Contains("codex joiner joined guarded"));
    }

    public ValueTask DisposeAsync() {
        anaHost.Dispose();
        brunoHost.Dispose();
        ana.Dispose();
        bruno.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
