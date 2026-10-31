using Pigeon.Core;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "retention")]
public sealed class RetentionSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-retention-" + Guid.NewGuid().ToString("N"));
    private readonly Principal sender = new("alpha", "sender");
    private readonly Principal first = new("alpha", "first");
    private readonly Principal second = new("alpha", "second");
    private Mailbox box = null!;
    private long now = 1000;
    private string acknowledged = "", queued = "";
    private string[] published = [];
    private int redacted;
    private string PathToDatabase => Path.Combine(directory, "retention.db");

    [Given("a retention store with registered agents")]
    public void Setup() {
        Directory.CreateDirectory(directory);
        box = Open();
        foreach (var p in new[] { sender, first, second })
            box.Register(p.Project, p.Agent, "mock", p.Agent + "-session");
    }

    private Mailbox Open() => new(PathToDatabase, () => now, maxAutomated: 1);

    [Given("an older store with an acknowledged message")]
    public void OldStore() {
        Directory.CreateDirectory(directory);
        using var db = new Sqlite(PathToDatabase);
        _ = db.Exec("CREATE TABLE agents(project TEXT NOT NULL, agent TEXT NOT NULL, runtime TEXT NOT NULL, session TEXT NOT NULL, PRIMARY KEY(project,agent))");
        _ = db.Exec("CREATE TABLE messages(id TEXT PRIMARY KEY, project TEXT NOT NULL, sender TEXT NOT NULL, recipient TEXT NOT NULL, body TEXT NOT NULL, correlation TEXT NOT NULL, reply_to TEXT, topic TEXT, idem TEXT NOT NULL, automated INTEGER NOT NULL, created INTEGER NOT NULL, expires INTEGER NOT NULL, status TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, lease_until INTEGER, UNIQUE(project,sender,idem), FOREIGN KEY(project,sender) REFERENCES agents(project,agent), FOREIGN KEY(project,recipient) REFERENCES agents(project,agent))");
        _ = db.Exec("INSERT INTO agents VALUES('alpha','sender','mock','sender-session')");
        _ = db.Exec("INSERT INTO agents VALUES('alpha','first','mock','first-session')");
        _ = db.Exec("INSERT INTO messages(id,project,sender,recipient,body,correlation,idem,automated,created,expires,status) VALUES('old','alpha','sender','first','old body','old thread','D:old-key',0,10,100000,'acknowledged')");
    }

    [When("the older store is opened by the current service")]
    public void OpenOldStore() => box = Open();

    [Then("the old message is preserved with a fresh retention window")]
    public void OldPreserved() {
        Assert.Equal("acknowledged", box.Status(sender, "old"));
        Assert.Equal(1, box.Inbox(first).Acknowledged);
        Assert.Equal(0, box.RedactTerminalBodies(sender, 1000));
        using var db = new Sqlite(PathToDatabase);
        Assert.Equal("old body", db.Query("SELECT body FROM messages WHERE id='old'")[0][0]);
    }

    [Then("the old message body is redacted and its idempotency key remains valid")]
    public void OldRedacted() {
        Assert.Equal(1, redacted);
        using var db = new Sqlite(PathToDatabase);
        Assert.Equal("", db.Query("SELECT body FROM messages WHERE id='old'")[0][0]);
        Assert.Equal("old", box.Send(sender, first.Agent, "old body", "old thread", null, "old-key", 100000));
    }

    [AfterScenario]
    public void Cleanup() {
        box?.Dispose();
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    [When("an automated message is acknowledged and another remains queued")]
    public void DirectMessages() {
        acknowledged = box.Send(sender, first.Agent, "private ping", "bounded", null, "ping", 100000, true);
        Assert.True(box.Ack(first, box.Receive(first)!.Receipt));
        queued = box.Send(sender, second.Agent, "queued body", "other", null, "queued", 100000);
    }

    [When("a publication is acknowledged by one of two subscribers")]
    public void Publish() {
        box.CreateTopic(sender, "design");
        box.Subscribe(first, "design");
        box.Subscribe(second, "design");
        published = box.Publish(sender, "design", "private publication", "design-thread", "publication", 100000);
        Assert.True(box.Ack(first, box.Receive(first)!.Receipt));
    }

    [When("the retention store is reopened")]
    public void Reopen() {
        box.Dispose();
        box = Open();
    }

    [When("the retention window elapses")]
    public void Advance() => now += 2000;

    [When("terminal bodies are redacted")]
    public void Redact() => redacted = box.RedactTerminalBodies(sender, 1000);

    [Then("only the acknowledged content is redacted")]
    public void DirectRedacted() {
        Assert.Equal(1, redacted);
        using var db = new Sqlite(PathToDatabase);
        Assert.Equal("", db.Query("SELECT body FROM messages WHERE id=?", acknowledged)[0][0]);
        Assert.Equal("queued body", db.Query("SELECT body FROM messages WHERE id=?", queued)[0][0]);
        Assert.Equal("acknowledged", box.Status(sender, acknowledged));
    }

    [Then("the original idempotency and automated exchange limit still apply")]
    public void Idempotency() {
        Assert.Equal(acknowledged, box.Send(sender, first.Agent, "private ping", "bounded", null, "ping", 100000, true));
        Assert.Throws<InvalidOperationException>(() => box.Send(sender, first.Agent, "changed", "bounded", null, "ping", 100000, true));
        Assert.Throws<InvalidOperationException>(() => box.Send(sender, first.Agent, "another", "bounded", null, "another", 100000, true));
    }

    [Then("the queued content can still be polled")]
    public void QueuedAvailable() => Assert.Equal("queued body", box.Receive(second)!.Body);

    [Then("no publication content is redacted")]
    public void PublicationPreserved() {
        Assert.Equal(0, redacted);
        using var db = new Sqlite(PathToDatabase);
        Assert.All(db.Query("SELECT body FROM messages WHERE topic='design'"), row => Assert.Equal("private publication", row[0]));
        Assert.Equal("private publication", db.Query("SELECT body FROM publications")[0][0]);
    }

    [When("the other subscriber acknowledges the publication")]
    public void AckSecond() => Assert.True(box.Ack(second, box.Receive(second)!.Receipt));

    [Then("the publication content is redacted for both subscribers")]
    public void PublicationRedacted() {
        Assert.Equal(2, redacted);
        using var db = new Sqlite(PathToDatabase);
        Assert.All(db.Query("SELECT body FROM messages WHERE topic='design'"), row => Assert.Equal("", row[0]));
        Assert.Equal("", db.Query("SELECT body FROM publications")[0][0]);
    }

    [Then("a retry returns the original publication deliveries")]
    public void PublicationIdempotency() => Assert.Equal(published, box.Publish(sender, "design", "private publication", "design-thread", "publication", 100000));
}
