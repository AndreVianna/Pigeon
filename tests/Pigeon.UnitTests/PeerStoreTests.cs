using Pigeon.Core;

using Xunit;

namespace Pigeon.UnitTests;

// Two mailboxes stand in for Ana's and Bruno's services. Relay() does what the peer link does:
// deliver each due outbox item to the other side and report the outcome back. Link() does what an
// allowed join does on each side.
public sealed class PeerStoreTests : IDisposable {
    private const string AnaPrint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BrunoPrint = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-peers-" + Guid.NewGuid().ToString("N"));
    private long now = 1_000_000;
    private readonly Mailbox ana, bruno;

    public PeerStoreTests() {
        Directory.CreateDirectory(directory);
        ana = new Mailbox(Path.Combine(directory, "ana.db"), () => now);
        bruno = new Mailbox(Path.Combine(directory, "bruno.db"), () => now);
        _ = ana.AddPeer(BrunoPrint, "bruno-desktop", "10.0.0.2:49374");
        _ = bruno.AddPeer(AnaPrint, "ana-laptop", "10.0.0.1:49374");
    }

    private int Relay(Mailbox from, string fromPrint, Mailbox to, string toPrint) {
        var delivered = 0;
        foreach (var item in from.DueOutbox(toPrint)) {
            var known = item.Kind switch {
                "leave" => to.ReceiveLeave(fromPrint, item.Offered) || true,
                "forget" => to.ReceiveForget(fromPrint) || true,
                _ => to.ReceiveFromPeer(fromPrint, item.Offered, item.MessageId!, item.SenderTool, item.Body!, item.ExpiresUnixMs - now)
            };
            if (known) {
                from.OutboxDelivered(item.Id);
                delivered++;
            }
            else
                from.OutboxRejected(item.Id);
        }

        return delivered;
    }

    private long Expires => now + 86400000;

    // Ana opens "review" to other machines and Bruno's session is allowed in.
    private void Link() {
        _ = ana.Join("claude-code", "ana-session", "review", remote: true);
        Assert.Equal(["review"], ana.ListedTopics());
        _ = ana.LinkTopic(BrunoPrint, "review", "codex");
        _ = bruno.LinkJoin(AnaPrint, "review", "claude-code", "bruno-session", "codex");
    }

    private static string[] Bodies(Mailbox box, string session, string topic) => [.. box.Take(session, topic).Select(d => d.Body)];

    [Fact]
    public void A_remote_topic_carries_messages_both_ways_and_each_side_sees_the_other_as_the_machine() {
        Link();
        Assert.Empty(ana.ListedTopics());
        // Both sides were told who is on the other machine.
        Assert.Contains(ana.Take("ana-session", "review"), d => d.SenderTool == "pigeon" && d.Body.Contains("codex @bruno-desktop joined review"));
        Assert.Contains(bruno.Take("bruno-session", "review@ana-laptop"), d => d.Body.Contains("You joined review@ana-laptop"));

        _ = ana.Send("ana-session", "review", "hello from Ana", "k1", Expires);
        Assert.Equal(1, Relay(ana, AnaPrint, bruno, BrunoPrint));
        var received = Assert.Single(bruno.Take("bruno-session", "review@ana-laptop"));
        Assert.Equal(("claude-code", "@ana-laptop", "hello from Ana"), (received.SenderTool, received.SenderSessionId, received.Body));

        _ = bruno.Send("bruno-session", "review@ana-laptop", "hi Ana", "k2", Expires);
        Assert.Equal(1, Relay(bruno, BrunoPrint, ana, AnaPrint));
        var back = Assert.Single(ana.Take("ana-session", "review"));
        Assert.Equal(("codex", "@bruno-desktop", "hi Ana"), (back.SenderTool, back.SenderSessionId, back.Body));
        Assert.Empty(ana.DueOutbox(BrunoPrint));
    }

    [Fact]
    public void A_message_waits_in_the_outbox_while_the_machine_is_unreachable_and_is_stored_once() {
        Link();
        _ = ana.Send("ana-session", "review", "while Bruno is away", "k1", Expires);
        var item = Assert.Single(ana.DueOutbox(BrunoPrint));
        ana.OutboxFailed(item.Id);
        Assert.Empty(ana.DueOutbox(BrunoPrint));
        now += 10000;
        item = Assert.Single(ana.DueOutbox(BrunoPrint));
        // The same message delivered twice is stored once.
        Assert.True(bruno.ReceiveFromPeer(AnaPrint, item.Offered, item.MessageId!, item.SenderTool, item.Body!, 60000));
        Assert.True(bruno.ReceiveFromPeer(AnaPrint, item.Offered, item.MessageId!, item.SenderTool, item.Body!, 60000));
        Assert.Equal(["while Bruno is away"], Bodies(bruno, "bruno-session", "review@ana-laptop").Where(b => !b.StartsWith("You joined", StringComparison.Ordinal)));
    }

    [Fact]
    public void Only_topics_opened_to_other_machines_with_a_free_seat_are_listed_or_reachable() {
        _ = ana.Join("claude-code", "ana-session", "private");
        _ = ana.Join("claude-code", "ana-session", "open", remote: true);
        _ = ana.Join("claude-code", "ana-2", "full", remote: true);
        _ = ana.Join("codex", "ana-3", "full");
        Assert.Equal(["open"], ana.ListedTopics());
        Assert.Throws<InvalidOperationException>(() => ana.LinkTopic(BrunoPrint, "private", "codex"));
        Assert.Throws<InvalidOperationException>(() => ana.LinkTopic(BrunoPrint, "full", "codex"));
        Assert.False(ana.ReceiveFromPeer(BrunoPrint, "private", "m1", "codex", "let me in", 60000));
        Assert.Throws<InvalidOperationException>(() => ana.LinkTopic(new string('c', 64), "open", null));
        // Only the session that opens a topic decides that it is open to other machines.
        Assert.Throws<InvalidOperationException>(() => ana.Join("codex", "late", "private", remote: true));
        Assert.Throws<InvalidOperationException>(() => ana.Join("codex", "late", "x@bruno-desktop"));
    }

    [Fact]
    public void Leaving_ends_the_topic_on_both_machines_and_the_open_topic_is_listed_again() {
        Link();
        _ = bruno.Leave("bruno-session", "review@ana-laptop");
        Assert.Empty(bruno.Shares());
        Assert.Equal(1, Relay(bruno, BrunoPrint, ana, AnaPrint));
        Assert.Empty(ana.Shares());
        Assert.Equal(["review"], ana.ListedTopics());
        Assert.Contains(ana.Take("ana-session", "review"), d => d.Body.Contains("@bruno-desktop left review"));

        // From the other side: Ana leaving deletes Bruno's copy.
        _ = ana.LinkTopic(BrunoPrint, "review", "codex");
        _ = bruno.LinkJoin(AnaPrint, "review", "claude-code", "bruno-session", "codex");
        Assert.True(ana.Leave("ana-session", "review"));
        Assert.Equal(1, Relay(ana, AnaPrint, bruno, BrunoPrint));
        Assert.Empty(bruno.Topics());
    }

    [Fact]
    public void Removing_a_machine_ends_its_topics_and_forgets_it() {
        Link();
        Assert.True(ana.RemovePeer("bruno-desktop"));
        Assert.Empty(ana.Shares());
        Assert.Null(ana.PeerByFingerprint(BrunoPrint));
        Assert.Equal(["review"], ana.ListedTopics());
        Assert.False(ana.RemovePeer("bruno-desktop"));
    }

    // Found in the two-machine test on 2026-11-07: the removed side kept the topic and retried for a day.
    [Fact]
    public void Removing_a_machine_tells_it_so_it_ends_the_topic_and_forgets_this_machine_too() {
        Link();
        Assert.True(ana.RemovePeer("bruno-desktop"));
        Assert.Empty(ana.Peers());
        var farewell = Assert.Single(ana.Farewells());
        Assert.Equal((BrunoPrint, "10.0.0.2:49374"), (farewell.Fingerprint, farewell.Address));
        Assert.Equal(1, Relay(ana, AnaPrint, bruno, BrunoPrint));
        ana.FarewellDone(BrunoPrint);
        Assert.Empty(ana.Farewells());
        // Bruno's side ended the topic, was told why, and forgot Ana.
        Assert.Empty(bruno.Peers());
        Assert.DoesNotContain(bruno.Topics(), t => t.Topic == "review@ana-laptop");
    }

    [Fact]
    public void Joining_again_cancels_a_farewell_that_was_not_delivered_yet() {
        Link();
        _ = ana.RemovePeer("bruno-desktop");
        _ = ana.AddPeer(BrunoPrint, "bruno-desktop", "10.0.0.2:49374");
        Assert.Empty(ana.Farewells());
        Assert.DoesNotContain(ana.DueOutbox(BrunoPrint), i => i.Kind == "forget");
    }

    [Fact]
    public void Machine_names_are_validated_and_a_taken_name_gets_a_number() {
        Assert.Throws<ArgumentException>(() => ana.AddPeer("not-hex", "x", null));
        Assert.Throws<ArgumentException>(() => ana.AddPeer(new string('c', 64), "Bad Name", null));
        Assert.Equal("bruno-desktop", ana.AddPeer(BrunoPrint, "another-name", "10.0.0.9:49374").Name);
        Assert.Equal("10.0.0.9:49374", ana.PeerByFingerprint(BrunoPrint)!.Address);
        Assert.Equal("bruno-desktop-2", ana.AddPeer(new string('c', 64), "bruno-desktop", null).Name);
        Assert.Equal("desktop-ab12", Mailbox.TidyName("DESKTOP-AB12"));
        Assert.Equal("anas-macbook-pro", Mailbox.TidyName("Ana's MacBook Pro.local"));
    }

    [Fact]
    public void A_message_from_the_other_machine_keeps_only_the_lifetime_it_had_left() {
        Link();
        _ = bruno.Take("bruno-session", "review@ana-laptop");
        Assert.True(bruno.ReceiveFromPeer(AnaPrint, "review", "m-short", "claude-code", "soon gone", 5000));
        now += 6000;
        Assert.Empty(bruno.Take("bruno-session", "review@ana-laptop"));
    }

    public void Dispose() {
        ana.Dispose();
        bruno.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
