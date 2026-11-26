using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// The parts of the peer link that need no other machine: the join code and addresses. The
// listener's boundaries over real TLS are in tests/Pigeon.IntegrationTests/Features/PeerTls.feature.
public sealed class PeerLinkTests {
    [Fact]
    public void The_join_code_depends_on_both_values_both_machines_and_the_topic() {
        var (a, b) = (JoinCode.NewNonce(), JoinCode.NewNonce());
        var code = JoinCode.Derive(a, b, "listing", "requester", "topic");
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(code, JoinCode.Derive(a, b, "listing", "requester", "topic"));
        Assert.NotEqual(code, JoinCode.Derive(a, b, "other", "requester", "topic"));
        Assert.NotEqual(code, JoinCode.Derive(a, b, "listing", "requester", "other"));
        Assert.True(JoinCode.Matches(JoinCode.Commit(a), a));
        Assert.False(JoinCode.Matches(JoinCode.Commit(a), b));
    }

    [Fact]
    public void Addresses_accept_host_names_and_default_the_port() {
        Assert.Equal("192.168.2.20:29374", PeerHost.ParseTarget("192.168.2.20"));
        Assert.Equal("dev-laptop.tail1234.ts.net:29374", PeerHost.ParseTarget("Dev-Laptop.tail1234.ts.net"));
        Assert.Equal("dev-laptop:50000", PeerHost.ParseTarget("dev-laptop:50000"));
        Assert.Throws<ArgumentException>(() => PeerHost.ParseTarget("bad host!"));
        Assert.Throws<ArgumentException>(() => PeerHost.ParseListen("dev-laptop"));
    }
}
