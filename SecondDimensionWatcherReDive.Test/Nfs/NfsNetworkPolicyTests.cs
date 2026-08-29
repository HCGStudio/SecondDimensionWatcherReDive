using System.Net;
using SecondDimensionWatcherReDive.NFS.Server;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public sealed class NfsNetworkPolicyTests
{
    [TestMethod]
    public void AllowsOnlyConfiguredNetworks()
    {
        var policy = new NfsNetworkPolicy(["192.168.50.0/24", "::1/128"]);

        Assert.IsTrue(policy.IsAllowed(IPAddress.Parse("192.168.50.42")));
        Assert.IsTrue(policy.IsAllowed(IPAddress.IPv6Loopback));
        Assert.IsFalse(policy.IsAllowed(IPAddress.Parse("192.168.51.42")));
        Assert.IsFalse(policy.IsAllowed(IPAddress.Parse("203.0.113.1")));
    }

    [TestMethod]
    public void InvalidCidrFailsClosed()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new NfsNetworkPolicy(["not-a-network"]));
    }
}
