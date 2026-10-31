using Pigeon.Core;
using Reqnroll;
using Xunit;

namespace Pigeon.IntegrationTests.Steps;

[Binding]
[Scope(Tag = "topic_migration")]
public sealed class TopicMigrationSteps {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-migration-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(directory, "messages.db");
    private Exception? failure;

    [BeforeScenario] public void Setup() => Directory.CreateDirectory(directory);
    [AfterScenario] public void Cleanup() => Directory.Delete(directory, recursive: true);

    [Given("a legacy database with distinct topics and identities")]
    public void DistinctLegacy() {
        using var box = new Mailbox(Database, () => 1000);
        box.Register("old-a", "ana", "codex", "a");
        box.Register("old-a", "bob", "claude_code", "b");
        box.Register("old-b", "charlie", "codex", "c");
        box.CreateTopic(new Principal("old-a", "ana"), "pigeon");
        box.CreateTopic(new Principal("old-b", "charlie"), "sigma");
        box.Subscribe(new Principal("old-a", "bob"), "pigeon");
        _ = box.Publish(new Principal("old-a", "ana"), "pigeon", "saved", "conversation", "key", 100000);
    }
    [When("the database is opened in topic mode")]
    public void Migrate() { using var box = new Mailbox(Database, () => 1000); box.EnableTopicMode(); }
    [Then("both topics and the queued delivery are available")]
    public void Preserved() {
        using var box = new Mailbox(Database, () => 1000);
        box.EnableTopicMode();
        Assert.Equal(["pigeon", "sigma"], box.TopicsForProject(Mailbox.TopicNamespace));
        var delivery = box.Receive(new Principal(Mailbox.TopicNamespace, "bob"), topic: "pigeon");
        Assert.NotNull(delivery);
        Assert.Equal("saved", delivery.Body);
    }
    [Given("a legacy database with conflicting topic names")]
    public void ConflictingLegacy() {
        using var box = new Mailbox(Database, () => 1000);
        box.CreateTopicForProject("old-a", "shared");
        box.CreateTopicForProject("old-b", "shared");
    }
    [Given("a legacy database with conflicting agent names")]
    public void ConflictingAgents() {
        using var box = new Mailbox(Database, () => 1000);
        box.Register("old-a", "ana", "codex", "session-a");
        box.Register("old-b", "ana", "codex", "session-b");
    }
    [When("migration is attempted")]
    public void TryMigrate() {
        using var box = new Mailbox(Database, () => 1000);
        failure = Record.Exception(box.EnableTopicMode);
    }
    [Then("migration fails and the legacy topics remain unchanged")]
    public void ConflictPreserved() {
        Assert.IsType<InvalidOperationException>(failure);
        using var box = new Mailbox(Database, () => 1000);
        Assert.Equal(["shared"], box.TopicsForProject("old-a"));
        Assert.Equal(["shared"], box.TopicsForProject("old-b"));
    }
    [Then("migration fails and both legacy agent bindings remain unchanged")]
    public void ConflictingAgentsPreserved() {
        Assert.IsType<InvalidOperationException>(failure);
        using var box = new Mailbox(Database, () => 1000);
        Assert.Equal("session-a", box.Identity(new Principal("old-a", "ana")).SessionId);
        Assert.Equal("session-b", box.Identity(new Principal("old-b", "ana")).SessionId);
    }
}
