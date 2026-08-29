using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class RefreshTokenStoreTests
{
    [TestMethod]
    public async Task RotationIsSingleUseAndReplayRevokesTheFamily()
    {
        var store = CreateStore();
        var first = await store.IssueAsync("jwt-1", null, CancellationToken.None);
        Assert.IsNotNull(first);

        var family = await store.ConsumeAsync(first.Token, "jwt-1", CancellationToken.None);
        Assert.IsNotNull(family);
        var second = await store.IssueAsync("jwt-2", family, CancellationToken.None);
        Assert.IsNotNull(second);

        Assert.IsNull(await store.ConsumeAsync(first.Token, "jwt-1", CancellationToken.None));
        Assert.IsNull(await store.ConsumeAsync(second.Token, "jwt-2", CancellationToken.None));
    }

    [TestMethod]
    public async Task ConcurrentRotationAllowsAtMostOneConsumer()
    {
        var store = CreateStore();
        var issued = await store.IssueAsync("jwt", null, CancellationToken.None);
        Assert.IsNotNull(issued);

        var results = await Task.WhenAll(
            store.ConsumeAsync(issued.Token, "jwt", CancellationToken.None),
            store.ConsumeAsync(issued.Token, "jwt", CancellationToken.None));

        Assert.AreEqual(1, results.Count(result => result is not null));
        var family = results.Single(result => result is not null)!;
        Assert.IsNull(await store.IssueAsync("next", family, CancellationToken.None));
    }

    [TestMethod]
    public async Task LogoutRevokesOutstandingRefreshToken()
    {
        var store = CreateStore();
        var issued = await store.IssueAsync("jwt", null, CancellationToken.None);
        Assert.IsNotNull(issued);

        await store.RevokeAsync(issued.Token, CancellationToken.None);

        Assert.IsNull(await store.ConsumeAsync(issued.Token, "jwt", CancellationToken.None));
    }

    private static RefreshTokenStore CreateStore()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        return new RefreshTokenStore(
            cache,
            Options.Create(new TokenSecurityOptions { RefreshTokenDays = 30 }),
            TimeProvider.System);
    }
}
