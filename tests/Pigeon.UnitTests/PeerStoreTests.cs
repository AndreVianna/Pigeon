using Pigeon.Core;

using Xunit;

namespace Pigeon.UnitTests;

// Two mailboxes stand in for Ana's and Bruno's services. Relay() does what the peer link will do:
// deliver each due outbox item to the other side and report the outcome back.
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

    // Delivers every due item from one side to the other; returns how many were delivered.
    private static int Relay(Mailbox from, string fromPrint, Mailbox to, string toPrint) {
        var delivered = 0;
        foreach (var item in from.DueOutbox(toPrint)) {
            var known = item.Kind switch {
                "offer" => to.AcceptOffer(fromPrint, item.Offered, item.SenderTool) is not null,
                "unshare" => to.ReceiveUnshare(fromPrint, item.Offered) || true,
                _ => to.ReceiveFromPeer(fromPrint, item.Offered, item.MessageId!, item.SenderTool, item.Body!, item.ExpiresUnixMs)
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

    [Fact]
    public void A_shared_topic_carries_messages_both_ways_and_each_side_sees_the_other_as_the_peer() {
        _ = ana.Join("claude-code", "ana-session", "review");
        _ = ana.Share("review", "bruno-desktop");
        Assert.Equal(1, Relay(ana, AnaPrint, bruno, BrunoPrint));
        var joined = bruno.Join("codex", "bruno-session", "review@ana-laptop");
        // Bruno sees Ana's member as the peer with her tool name, never her session ID.
        Assert.Contains(joined.Members, m => m.SessionId == "@ana-laptop" && m.Tool == "claude-code");

        var sent = ana.Send("ana-session", "review", "hello from Ana", "k1", Expires);
        Assert.Equal("@bruno-desktop", sent.Recipient!.SessionId);
        Assert.Equal(1, Relay(ana, AnaPrint, bruno, BrunoPrint));
        var received = Assert.Single(bruno.Take("bruno-session", "review@ana-laptop"));
        Assert.Equal(("hello from Ana", "@ana-laptop", "claude-code"), (received.Body, received.SenderSessionId, received.SenderTool));
        Assert.Equal(sent.MessageId, received.Id);

        _ = bruno.Send("bruno-session", "review@ana-laptop", "hi Ana", "k2", Expires);
        Assert.Equal(1, Relay(bruno, BrunoPrint, ana, AnaPrint));
        var reply = Assert.Single(ana.Take("ana-session", "review"));
        Assert.Equal(("hi Ana", "@bruno-desktop", "codex"), (reply.Body, reply.SenderSessionId, reply.SenderTool));
        // A delivered message is done on the sending side; nothing is left to send.
        Assert.Empty(ana.DueOutbox(BrunoPrint));
        Assert.Equal(0, ana.OutboxCount(BrunoPrint));
    }

    [Fact]
    public void A_message_waits_in_the_outbox_while_the_peer_is_unreachable_and_is_stored_once()  {
        _ = ana.Join("claude-code", "ana-session", "review");
        _ = ana.Share("review", "bruno-desktop");
        _ = Relay(ana, AnaPrint, bruno, BrunoPrint);
        _ = ana.Send("ana-session", "review", "while away", "k1", Expires);
        var item = Assert.Single(ana.DueOutbox(BrunoPrint));
        ana.OutboxFailed(item.Id);
        Assert.Empty(ana.DueOutbox(BrunoPrint));
        now += 5000;
        item = Assert.Single(ana.DueOutbox(BrunoPrint));
        // The first attempt reached Bruno but its acknowledgment was lost; the retry is a no-op there.
        Assert.True(bruno.ReceiveFromPeer(AnaPrint, item.Offered, item.MessageId!, item.SenderTool, item.Body!, item.ExpiresUnixMs));
        Assert.Equal(1, Relay(ana, AnaPrint, bruno, BrunoPrint));
        // Nobody on Bruno's side joined yet: the message waits for whoever joins.
        var joined = bruno.Join("codex", "bruno-session", "review@ana-laptop");
        Assert.Equal(1, joined.Waiting);
        Assert.Single(bruno.Take("bruno-session", "review@ana-laptop"));
    }

    [Fact]
    public void Nothing_is_reachable_until_a_person_shares_it() {
        // An unshared name, and a name that only looks shared, cannot be joined or created.
        Assert.Throws<InvalidOperationException>(() => bruno.Join("codex", "bruno-session", "secret@ana-laptop"));
        Assert.Throws<InvalidOperationException>(() => ana.Join("codex", "x", "made-up@bruno-desktop"));
        // A peer cannot deliver into a topic that was not shared with it.
        _ = ana.Join("claude-code", "ana-session", "private");
        Assert.False(ana.ReceiveFromPeer(BrunoPrint, "private", "m1", "codex", "let me in", Expires));
        Assert.Equal(0, ana.Topics("ana-session").Single().Waiting);
        // A local caller cannot pose as a remote seat.
        Assert.Throws<ArgumentException>(() => ana.Join("codex", "peer:" + BrunoPrint + ":private", "private"));
        // Unknown peers are refused.
        Assert.Throws<InvalidOperationException>(() => ana.AcceptOffer(new string('c', 64), "x", null));
    }

    [Fact]
    public void Sharing_is_refused_for_full_topics_shared_topics_and_unknown_peers() {
        _ = ana.Join("claude-code", "a1", "pair");
        _ = ana.Join("codex", "a2", "pair");
        Assert.Throws<InvalidOperationException>(() => ana.Share("pair", "bruno-desktop"));
        _ = ana.Join("claude-code", "a1", "review");
        _ = ana.Share("review", "bruno-desktop");
        Assert.Throws<InvalidOperationException>(() => ana.Share("review", "bruno-desktop"));
        Assert.Throws<InvalidOperationException>(() => ana.Share("review", "nobody"));
        Assert.Throws<InvalidOperationException>(() => ana.Share("missing", "bruno-desktop"));
        // The peer's seat fills the topic, so a second local session is refused.
        Assert.Throws<TopicFullException>(() => ana.Join("codex", "a3", "review"));
    }

    [Fact]
    public void The_tool_name_is_withheld_when_the_sharing_side_turns_it_off() {
        _ = ana.Join("claude-code", "ana-session", "review");
        _ = ana.Share("review", "bruno-desktop", shareTool: false);
        _ = Relay(ana, AnaPrint, bruno, BrunoPrint);
        _ = bruno.Join("codex", "bruno-session", "review@ana-laptop");
        _ = ana.Send("ana-session", "review", "hello", "k1", Expires);
        _ = Relay(ana, AnaPrint, bruno, BrunoPrint);
        var received = Assert.Single(bruno.Take("bruno-session", "review@ana-laptop"));
        Assert.Equal(("@ana-laptop", "remote"), (received.SenderSessionId, received.SenderTool));
    }

    [Fact]
    public void Unsharing_or_removing_the_peer_ends_the_topic_on_both_sides() {
        _ = ana.Join("claude-code", "ana-session", "review");
        _ = ana.Share("review", "bruno-desktop");
        _ = Relay(ana, AnaPrint, bruno, BrunoPrint);
        _ = bruno.Join("codex", "bruno-session", "review@ana-laptop");
        Assert.True(ana.Unshare("review"));
        _ = Relay(ana, AnaPrint, bruno, BrunoPrint);
        Assert.Empty(bruno.Topics("bruno-session"));
        // Ana keeps her topic, now with a free second seat.
        Assert.Single(ana.Topics("ana-session").Single().Members);

        _ = ana.Share("review", "bruno-desktop");
        _ = Relay(ana, AnaPrint, bruno, BrunoPrint);
        Assert.True(bruno.RemovePeer("ana-laptop"));
        Assert.Empty(bruno.Shares());
        Assert.Empty(bruno.Peers());
        // Removed: anything Ana still sends is refused, and the item is dropped on her side.
        _ = ana.Send("ana-session", "review", "anyone there?", "k3", Expires);
        Assert.Equal(0, Relay(ana, AnaPrint, bruno, BrunoPrint));
        Assert.Equal(0, ana.OutboxCount(BrunoPrint));
    }

    [Fact]
    public void Peer_names_and_fingerprints_are_validated_and_unique() {
        Assert.Throws<ArgumentException>(() => ana.AddPeer("not-hex", "x", null));
        Assert.Throws<ArgumentException>(() => ana.AddPeer(new string('c', 64), "Bad Name", null));
        Assert.Throws<InvalidOperationException>(() => ana.AddPeer(BrunoPrint, "another-name", null));
        Assert.Throws<InvalidOperationException>(() => ana.AddPeer(new string('c', 64), "bruno-desktop", null));
    }

    public void Dispose() {
        ana.Dispose();
        bruno.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
