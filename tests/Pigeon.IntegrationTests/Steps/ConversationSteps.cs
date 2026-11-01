using Pigeon.Core;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "conversations")]
public sealed class ConversationSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-conversations-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "messages.db");
    private long now = 1_000_000;
    private Mailbox store = null!;
    private int key;

    private const long Hour = 3_600_000;
    private string Send(string session, string topic, string text, string? retryKey = null) =>
        store.Send(session, topic, text, retryKey ?? "key-" + ++key, now + 24 * Hour).MessageId;
    private string[] Receive(string session, string topic) => [.. store.Take(session, topic).Select(m => m.Body)];

    [Given("a fresh message store")]
    public void Fresh() => Open();

    [Given("a fresh message store with a limit of {int} messages per hour")]
    public void Limited(int limit) => Open(limit);

    private void Open(int limit = 60) {
        Directory.CreateDirectory(directory);
        store = new Mailbox(Database, () => now, maxMessagesPerHour: limit);
    }

    [Then("the first session to join waits for a partner, the second completes the topic and a third is refused with both members named")]
    public void JoinRules() {
        Assert.Single(store.Join("codex", "A", "review").Members);
        var full = store.Join("Claude Code", "B", "review");
        Assert.Equal([new Member("codex", "A"), new Member("claude-code", "B")], full.Members);
        var refused = Assert.Throws<TopicFullException>(() => store.Join("codex", "C", "review"));
        Assert.Contains("codex A and claude-code B are in it", refused.Message);
        Assert.Contains("pigeon leave review as <session-id>", refused.Message);
    }

    [Then("joining again as a member changes nothing")]
    public void Rejoin() {
        Assert.Equal(2, store.Join("codex", "A", "review").Members.Length);
        Assert.Equal(["review"], store.Topics().Select(t => t.Topic));
    }

    [Then("a session that joined as codex cannot join another topic as claude-code")]
    public void ToolKept() {
        _ = store.Join("codex", "A", "first");
        var error = Assert.Throws<InvalidOperationException>(() => store.Join("claude-code", "A", "second"));
        Assert.Contains("already joined using codex", error.Message);
        Assert.Equal(["first"], store.Topics().Select(t => t.Topic));
    }

    [Then("a message sent to an empty seat is delivered to whoever joins second")]
    public void HeldForPartner() {
        _ = store.Join("codex", "A", "review");
        Assert.Null(store.Send("A", "review", "opening", "k1", now + Hour).Recipient);
        _ = store.Join("claude-code", "B", "review");
        Assert.Equal(1, store.GetWakeStatus("B").Queued);
        Assert.Equal(["opening"], Receive("B", "review"));
        // The sender never receives its own message.
        Assert.Empty(Receive("A", "review"));
    }

    [Then("three messages are received oldest first, and receiving again returns nothing")]
    public void Ordered() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        foreach (var text in new[] { "one", "two", "three" }) {
            Assert.Equal(new Member("claude-code", "B"), store.Send("A", "review", text, text, now + Hour).Recipient);
            now++;
        }
        var received = store.Take("B", "review");
        Assert.Equal(["one", "two", "three"], received.Select(m => m.Body));
        Assert.All(received, m => Assert.Equal(("review", "codex", "A"), (m.Topic, m.SenderTool, m.SenderSessionId)));
        Assert.Empty(Receive("B", "review"));
        Assert.Equal("completed", store.GetWakeStatus("B").State);
    }

    [Then("a member that leaves with unread messages passes them to the session that takes its place")]
    public void Replacement() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        _ = Send("A", "review", "for whoever reads the review");
        Assert.False(store.Leave("B", "review"));
        Assert.Equal(0, store.GetWakeStatus("B").Queued);
        _ = store.Join("claude-code", "B2", "review");
        Assert.Equal(["for whoever reads the review"], Receive("B2", "review"));
    }

    [Then("the topic and its unread messages are gone after both members leave")]
    public void LastLeaves() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        _ = Send("A", "review", "never read");
        Assert.False(store.Leave("B", "review"));
        Assert.True(store.Leave("A", "review"));
        Assert.Empty(store.Topics());
        _ = store.Join("claude-code", "B", "review");
        Assert.Empty(Receive("B", "review"));
        Assert.Throws<InvalidOperationException>(() => store.Leave("A", "review"));
    }

    [Then("a session outside a topic can neither send to it nor receive from it")]
    public void MembersOnly() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "other");
        Assert.Contains("is not in topic \"review\"", Assert.Throws<InvalidOperationException>(() => Send("B", "review", "intrusion")).Message);
        Assert.Throws<InvalidOperationException>(() => store.Take("B", "review"));
        Assert.Throws<InvalidOperationException>(() => Send("nobody", "review", "intrusion"));
    }

    [Then("resending with the same retry key returns the first message, and different text is refused")]
    public void RetryKey() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        var first = Send("A", "review", "once", "retry");
        Assert.Equal(first, Send("A", "review", "once", "retry"));
        Assert.Throws<InvalidOperationException>(() => Send("A", "review", "changed", "retry"));
        Assert.Equal(["once"], Receive("B", "review"));
    }

    [Then("an unread message is not delivered after its expiry")]
    public void Expiry() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        _ = store.Send("A", "review", "short-lived", "k", now + 1000);
        now += 2000;
        Assert.Empty(Receive("B", "review"));
        Assert.Equal("completed", store.GetWakeStatus("B").State);
    }

    [Then("the fourth message within an hour is refused and accepted again an hour later")]
    public void HourlyLimit() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        for (var i = 0; i < 3; i++) _ = Send(i % 2 == 0 ? "A" : "B", "review", "message " + i);
        Assert.Contains("limit of 3 messages per hour", Assert.Throws<InvalidOperationException>(() => Send("A", "review", "one too many")).Message);
        now += Hour + 1;
        _ = Send("A", "review", "later");
    }

    [Then("a topic whose members were not seen for the idle period is deleted, and a member seen recently keeps its topic")]
    public void Abandoned() {
        _ = store.Join("codex", "A", "quiet");
        _ = store.Join("claude-code", "B", "quiet");
        _ = Send("A", "quiet", "never read");
        _ = store.Join("codex", "C", "busy");
        now += 30 * 60000;
        store.Touch("C");
        now += 31 * 60000;
        Assert.Equal(["quiet"], store.DeleteAbandoned(Hour));
        Assert.Equal(["busy"], store.Topics().Select(t => t.Topic));
        Assert.Equal("completed", store.GetWakeStatus("B").State);
        // After a service restart every session gets a fresh grace period.
        now += 2 * Hour;
        store.TouchAll();
        Assert.Empty(store.DeleteAbandoned(Hour));
    }

    [Then("after the retention period a read message has no text and its retry key still prevents a duplicate")]
    public void Retention() {
        _ = store.Join("codex", "A", "review");
        _ = store.Join("claude-code", "B", "review");
        var id = Send("A", "review", "private text", "retry");
        _ = Send("A", "review", "unread text");
        Assert.Equal(["private text", "unread text"], Receive("B", "review"));
        _ = Send("A", "review", "still waiting");
        now += 2 * Hour;
        Assert.Equal(2, store.RedactTerminalBodies(Hour));
        Assert.Equal(id, Send("A", "review", "private text", "retry"));
        Assert.Throws<InvalidOperationException>(() => Send("A", "review", "other text", "retry"));
        Assert.Equal(["still waiting"], Receive("B", "review"));
    }

    [Given("an earlier database with a three-member topic and pending messages")]
    public void EarlierDatabase() {
        Directory.CreateDirectory(directory);
        using var old = new Sqlite(Database);
        foreach (var sql in new[] {
            "CREATE TABLE agents(project TEXT, agent TEXT, runtime TEXT, session TEXT, PRIMARY KEY(project,agent))",
            "CREATE TABLE session_routes(project TEXT, tool TEXT, session TEXT, agent TEXT)",
            "CREATE TABLE subscriptions(project TEXT, topic TEXT, agent TEXT)",
            "CREATE TABLE messages(id TEXT PRIMARY KEY, project TEXT, sender TEXT, recipient TEXT, body TEXT, topic TEXT, idem TEXT, created INTEGER, expires INTEGER, status TEXT)",
            "INSERT INTO session_routes VALUES('ns','codex','codex-1','a1'),('ns','claude-code','claude-1','a2'),('ns','codex','codex-2','a3')",
            // A name-based agent has no session route and is dropped.
            "INSERT INTO subscriptions VALUES('ns','design','a1'),('ns','design','a2'),('ns','design','a3'),('ns','design','legacy-name')",
            "INSERT INTO messages VALUES('m1','ns','a1','a2','pending for claude','design','k1',100,99999999999,'queued')",
            "INSERT INTO messages VALUES('m2','ns','a2','a1','already read','design','k2',200,99999999999,'acknowledged')",
            "INSERT INTO messages VALUES('m3','ns','legacy-name','a2','from a name','design','k3',300,99999999999,'queued')"
        })
            _ = old.Exec(sql);
    }

    [When("the message store opens it")]
    public void OpenEarlier() => Open();

    [Then("the two most recently active members remain, their pending message is kept, and the old tables are untouched")]
    public void Upgraded() {
        var topic = Assert.Single(store.Topics());
        Assert.Equal(["claude-1", "codex-1"], topic.Members.Select(m => m.SessionId).Order());
        Assert.Equal(["pending for claude"], Receive("claude-1", "design"));
        store.Dispose();
        using var check = new Sqlite(Database);
        Assert.Equal("4", check.Query("SELECT COUNT(*) FROM subscriptions")[0][0]);
        Assert.Equal("design: codex-2", check.Query("SELECT value FROM pigeon_metadata WHERE key='upgrade_removed_members'")[0][0]);
    }

    [Then("a write during another process's lock fails as storage unavailable and succeeds after it is released")]
    public void HeldLock() {
        _ = store.Join("codex", "A", "review");
        using (var other = new Sqlite(Database)) {
            _ = other.Exec("BEGIN IMMEDIATE");
            Assert.Throws<StorageUnavailableException>(() => store.Join("claude-code", "B", "review"));
            _ = other.Exec("ROLLBACK");
        }
        Assert.Equal(2, store.Join("claude-code", "B", "review").Members.Length);
    }

    [AfterScenario]
    public void Cleanup() {
        store?.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
