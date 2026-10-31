using Xunit;
using static MonitorParent;

namespace Pigeon.UnitTests;

public class MonitorParentTests {
    private static Ancestor Process(string image, long createdAt = 100, bool alive = true) => new(image, createdAt, alive);

    [Theory]
    [InlineData(@"C:\Program Files\Git\bin\bash.exe")]
    [InlineData(@"C:\Program Files\Git\usr\bin\BASH.EXE")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    public void A_shell_is_watched_and_the_walk_continues(string image) =>
        Assert.Equal(Link.Continue, Classify(Process(image), childCreatedAt: 200, depth: 0));

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Roaming\Claude\claude-code\claude.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    public void The_first_non_shell_ancestor_is_watched_last(string image) =>
        Assert.Equal(Link.Last, Classify(Process(image), childCreatedAt: 200, depth: 1));

    [Fact]
    public void An_ancestor_that_cannot_be_opened_ends_the_chain() =>
        Assert.Equal(Link.Stop, Classify(null, childCreatedAt: 200, depth: 1));

    [Fact]
    public void An_exited_ancestor_ends_the_chain() =>
        Assert.Equal(Link.Stop, Classify(Process("bash.exe", alive: false), childCreatedAt: 200, depth: 0));

    [Fact]
    public void A_reused_parent_id_started_after_its_child_ends_the_chain() =>
        Assert.Equal(Link.Stop, Classify(Process("claude.exe", createdAt: 300), childCreatedAt: 200, depth: 0));

    [Fact]
    public void The_walk_is_bounded() =>
        Assert.Equal(Link.Last, Classify(Process("bash.exe"), childCreatedAt: 200, depth: MaxDepth - 1));
}
