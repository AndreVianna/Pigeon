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
            foreach (var who in new[] { "ana", "bruno", "carol", "bruno-reinstalled" })
                ServiceIdentity.LoadOrCreate(Store(who)).Dispose();
        }

        public IIdentityStore Store(string who) => new FileIdentityStore(Path.Combine(directory, who));

        public void Dispose() {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private const string AnaAt = "192.0.2.1:29374", BrunoAt = "192.0.2.2:29374", CarolAt = "192.0.2.3:29374";
    private static readonly string Bridge = Guid.NewGuid().ToString("N");
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-join-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryPeerNetwork network = new();
    private long now = 1_790_000_000_000;
    private Broker ana = null!, bruno = null!;
    private PeerHost anaHost = null!, brunoHost = null!;
    private readonly List<(Broker Broker, PeerHost Host)> machines = [];

    public async ValueTask InitializeAsync() {
        Directory.CreateDirectory(directory);
        (ana, anaHost) = await Machine("ana", "ana-laptop", AnaAt);
        (bruno, brunoHost) = await Machine("bruno", "bruno-desktop", BrunoAt);
    }

    // A named machine listening at address: the setting `pigeon peer listen` records, and its
    // listener's protocol on the in-memory network instead of a socket.
    private async Task<(Broker, PeerHost)> Machine(string who, string name, string address, string? identity = null) {
        var broker = new Broker(Path.Combine(directory, who + ".db"), () => now);
        var host = new PeerHost(broker, identities.Store(identity ?? who), network.From(address));
        Assert.True((await host.ExecuteAsync("peer_name", Args(new { name }))).Ok);
        broker.SetPeerSetting("listen", address);
        network.Listen(address, host.Server());
        machines.Add((broker, host));
        return (broker, host);
    }

    // The service stops and starts again with the same database; with another identity, as after a
    // reinstall that kept the machine's name and address.
    private async Task<(Broker, PeerHost)> Restart(Broker broker, PeerHost host, string who, string name, string address, string? identity = null) {
        _ = machines.Remove((broker, host));
        host.Dispose();
        broker.Dispose();
        return await Machine(who, name, address, identity);
    }

    // Two seconds pass on both machines: each contacts the machines it was asked to, asks about its
    // open requests and delivers what is due, as the peer host does on its own every second.
    private async Task Pass() {
        now += 2000;
        foreach (var (_, host) in machines.ToArray())
            await host.PassAsync(TestContext.Current.CancellationToken);
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
    private Task FirstJoin() => FirstJoin(bruno, "bruno-session", "bruno-desktop", "ana-session", "review");

    private async Task FirstJoin(Broker joiner, string joinerSession, string joinerName, string anaSession, string topic) {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = anaSession, topic, remote = true });
        var pending = Call(joiner, "join", new { tool = "codex", sessionId = joinerSession, topic = $"{topic}@{AnaAt}" });
        Assert.True(pending.GetProperty("pending").GetBoolean());
        Assert.Contains("Do not call pigeon_join again", pending.GetProperty("text").GetString());
        Assert.Equal("contacting", Answer(joiner, Prompt(joiner, joinerSession, "confirm"), "accept"));
        await Pass();
        var shown = Prompt(joiner, joinerSession, "show");
        var code = Digits().Match(shown.GetProperty("message").GetString()!).Value;
        Assert.Equal(code, Call(joiner, "request_show", new { id = pending.GetProperty("request").GetString() }).GetProperty("code").GetString());

        var asked = Prompt(ana, anaSession, "code");
        Assert.Contains("a new machine, " + joinerName, asked.GetProperty("message").GetString());
        Assert.True(asked.GetProperty("code").GetBoolean());
        Assert.Equal("wrong code", Answer(ana, asked, "accept", Wrong(code)));
        Assert.Equal("allowed", Answer(ana, asked, "accept", code));
        await Pass();
        Assert.Contains(Call(joiner, "topics", new { sessionId = joinerSession }).EnumerateArray(), t => t.GetProperty("Topic").GetString() == topic + "@ana-laptop");
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    private static string[] Ended(Broker broker) => [.. Call(broker, "requests", new { }).GetProperty("ended").EnumerateArray().Select(e => e.GetProperty("text").GetString()!)];

    private static async Task<JsonElement> Status(PeerHost host) {
        var status = await host.ExecuteAsync("peer_status", Args(new { }));
        Assert.True(status.Ok, status.Error);
        return JsonSerializer.SerializeToElement(status.Value);
    }

    private static string? Identity(JsonElement status, string name) {
        var peer = Assert.Single(status.GetProperty("peers").EnumerateArray(), p => p.GetProperty("Name").GetString() == name);
        return peer.TryGetProperty("identity", out var identity) && identity.ValueKind == JsonValueKind.String ? identity.GetString() : null;
    }

    // Seen in the beta.3 test: the reinstalled machine's warning was only in the prompt, and the machine
    // that kept the old identity pinned was told nothing about why it could not reach it.
    [Fact]
    public async Task A_known_machine_with_another_identity_is_reported_and_never_trusted() {
        await FirstJoin();
        (bruno, brunoHost) = await Restart(bruno, brunoHost, "bruno", "bruno-desktop", BrunoAt, identity: "bruno-reinstalled");

        // The listing side: the reinstalled machine needs a code, and the person is warned.
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-2", topic = "r2", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-2", topic = "r2@ana-laptop" });
        await Pass();
        const string warning = "It calls itself bruno-desktop, the name of a machine you know, but its identity is different: it was reinstalled, or it is another machine. If it was reinstalled, remove the old one first: pigeon peer remove bruno-desktop.";
        Assert.Contains(warning, Prompt(ana, "ana-2", "code").GetProperty("message").GetString());
        var request = Assert.Single(Call(ana, "requests", new { }).GetProperty("incoming").EnumerateArray());
        Assert.True(request.GetProperty("needsCode").GetBoolean());
        Assert.Equal("bruno-desktop", request.TryGetProperty("changedName", out var changedName) ? changedName.GetString() : null);

        // The asking side: topics, delivery and a join all say why, and nothing reaches the machine.
        const string changed = "bruno-desktop's identity changed: it was reinstalled, or another machine answers at its address. Nothing is sent to it. If it was reinstalled, remove it with pigeon peer remove bruno-desktop and join again by address.";
        Assert.Equal(changed, (await anaHost.ExecuteAsync("peer_topics", Args(new { machine = "bruno-desktop" }))).Error);
        _ = Call(ana, "send", new { sessionId = "ana-session", topic = "review", message = "still there?" });
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-3", topic = "d3@bruno-desktop" });
        await Pass();
        var status = await Status(anaHost);
        Assert.Equal(changed, Identity(status, "bruno-desktop"));
        Assert.Equal(1, Assert.Single(status.GetProperty("peers").EnumerateArray()).GetProperty("outbox").GetInt32());
        Assert.Contains(changed, Ended(ana));
        Assert.DoesNotContain(Bodies(bruno, "bruno-session", "review@ana-laptop"), b => b.Contains("still there?"));

        // A new address clears the mark.
        Assert.True((await anaHost.ExecuteAsync("peer_address", Args(new { name = "bruno-desktop", address = "192.0.2.9" }))).Ok);
        Assert.Null(Identity(await Status(anaHost), "bruno-desktop"));
    }

    [Fact]
    public async Task A_known_machine_answering_at_another_known_machines_address_is_reported_as_moved() {
        await FirstJoin();
        var (carol, carolHost) = await Machine("carol", "carol-pc", CarolAt);
        await FirstJoin(carol, "carol-session", "carol-pc", "ana-2", "second");
        // Carol now answers at Bruno's address, as when two machines' addresses are swapped.
        network.Listen(BrunoAt, carolHost.Server());
        const string moved = "bruno-desktop's address now answers as carol-pc, another machine you know: the addresses moved. Nothing is sent to bruno-desktop. Set its new address with pigeon peer address bruno-desktop <address>.";
        Assert.Equal(moved, (await anaHost.ExecuteAsync("peer_topics", Args(new { machine = "bruno-desktop" }))).Error);
        Assert.Equal(moved, Identity(await Status(anaHost), "bruno-desktop"));
        Assert.Null(Identity(await Status(anaHost), "carol-pc"));

        // Any answer from the pinned identity clears the mark.
        network.Listen(BrunoAt, brunoHost.Server());
        Assert.True((await anaHost.ExecuteAsync("peer_topics", Args(new { machine = "bruno-desktop" }))).Ok);
        Assert.Null(Identity(await Status(anaHost), "bruno-desktop"));
    }

    // Seen in the beta.3 test: after a refusal, three wrong codes or a closed port, `pigeon requests`
    // on the asking side said only "No requests are waiting."
    [Fact]
    public async Task The_asking_side_keeps_the_outcome_of_a_refused_or_failed_request() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-session", topic = "d0", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-session", topic = $"d0@{AnaAt}" });
        _ = Answer(bruno, Prompt(bruno, "bruno-session", "confirm"), "accept");
        await Pass();
        var asked = Prompt(ana, "ana-session", "code");
        for (var i = 0; i < 3; i++)
            _ = Answer(ana, asked, "accept", "12345" + i);
        await Pass();

        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "ana-2", topic = "d3", remote = true });
        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-2", topic = $"d3@{AnaAt}" });
        _ = Answer(bruno, Prompt(bruno, "bruno-2", "confirm"), "accept");
        await Pass();
        Assert.Equal("denied", Answer(ana, Prompt(ana, "ana-2", "code"), "decline"));
        await Pass();

        _ = Call(bruno, "join", new { tool = "codex", sessionId = "bruno-3", topic = "x@192.0.2.9" });
        _ = Answer(bruno, Prompt(bruno, "bruno-3", "confirm"), "accept");
        await Pass();

        var requests = Call(bruno, "requests", new { });
        Assert.Empty(requests.GetProperty("outgoing").EnumerateArray());
        Assert.Equal([
            "Joining d0 on ana-laptop was refused after 3 wrong codes.",
            "The person at ana-laptop did not allow joining d3.",
            "Could not join x on 192.0.2.9:29374: 192.0.2.9:29374 could not be reached, or no Pigeon there accepts requests (it may have no topic open to other machines, or run an older Pigeon)."
        ], Ended(bruno));
        now += Broker.RequestMilliseconds;
        Assert.Empty(Ended(bruno));
    }

    // Seen in the beta.3 test: `pigeon allow` on a request that had ended printed "Ignored" and exited 0.
    [Fact]
    public void Allow_on_a_request_that_ended_is_an_error() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "opener", topic = "guarded", approval = true });
        var id = Call(ana, "join", new { tool = "codex", sessionId = "joiner", topic = "guarded" }).GetProperty("request").GetString()!;
        var code = Call(ana, "request_show", new { id }).GetProperty("code").GetString()!;
        for (var i = 0; i < 2; i++)
            Assert.Equal("wrong code", Call(ana, "request_allow", new { id, code = Wrong(code) }).GetProperty("result").GetString());
        Assert.Equal("denied after 3 wrong codes", Call(ana, "request_allow", new { id, code = Wrong(code) }).GetProperty("result").GetString());
        Assert.Equal($"Request {id} has ended; see pigeon requests.", Fail(ana, "request_allow", new { id, code }));
        Assert.Equal($"Request {id} has ended; see pigeon requests.", Fail(ana, "request_deny", new { id }));
        // A prompt's answer that a host replays is still ignored quietly.
        Assert.StartsWith("ignored", Call(ana, "prompt_answer", new { id = id + ":code", action = "accept", code }).GetProperty("result").GetString());
        Assert.DoesNotContain(Call(ana, "topics", new { sessionId = "joiner" }).EnumerateArray(), t => t.GetProperty("Topic").GetString() == "guarded");
    }

    // Seen in the beta.3 test: a request waiting when the service restarted vanished, and its joiner was
    // never told. Only the request's ID, session and topic are kept on disk, never its code.
    [Fact]
    public async Task A_request_waiting_when_the_service_restarts_is_ended_and_its_session_told() {
        _ = Call(ana, "join", new { tool = "claude-code", sessionId = "opener", topic = "t2", approval = true });
        var id = Call(ana, "join", new { tool = "codex", sessionId = "joiner", topic = "t2" }).GetProperty("request").GetString()!;
        var code = Call(ana, "request_show", new { id }).GetProperty("code").GetString()!;
        Assert.Contains(id, ana.PeerSetting("requests") ?? "");
        Assert.DoesNotContain(code, ana.PeerSetting("requests") ?? "");

        (ana, anaHost) = await Restart(ana, anaHost, "ana", "ana-laptop", AnaAt);
        const string text = "Your request to join t2 ended because the Pigeon service stopped. Join again to ask again.";
        var requests = Call(ana, "requests", new { });
        Assert.Empty(requests.GetProperty("local").EnumerateArray());
        Assert.Equal([text], Ended(ana));
        Assert.Null(ana.PeerSetting("requests"));
        var claim = Call(ana, "wake_claim", new { sessionId = "joiner", mode = "codex-queue" });
        Assert.Equal(JsonValueKind.Object, claim.ValueKind);
        var batch = Call(ana, "wake_receive", new { sessionId = "joiner", generation = claim.GetProperty("Generation").GetString() });
        Assert.Equal(text, batch.GetProperty("Messages")[0].GetProperty("Body").GetString());
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
        foreach (var (broker, host) in machines) {
            host.Dispose();
            broker.Dispose();
        }

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return ValueTask.CompletedTask;
    }
}
