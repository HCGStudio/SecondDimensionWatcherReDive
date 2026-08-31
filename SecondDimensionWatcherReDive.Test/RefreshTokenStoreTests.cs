using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class RefreshTokenStoreTests
{
    [TestMethod]
    public async Task ReplayAfterGraceRevokesTheFamily()
    {
        var time = new ManualTimeProvider();
        var store = CreateStore(new MemoryRefreshTokenStorage(), time);
        var first = await store.IssueAsync("jwt-1", CancellationToken.None);
        Assert.IsNotNull(first);

        var second = await store.RotateAsync(
            first.Token,
            "jwt-1",
            "jwt-2",
            CancellationToken.None);
        Assert.IsNotNull(second);

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.IsNull(await store.RotateAsync(
            first.Token,
            "jwt-1",
            "ignored",
            CancellationToken.None));
        Assert.IsNull(await store.RotateAsync(
            second.Token,
            "jwt-2",
            "jwt-3",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ConcurrentRotationAcrossInstancesReturnsOneIdempotentReplacement()
    {
        var time = new ManualTimeProvider();
        var sharedStorage = new MemoryRefreshTokenStorage();
        var firstStore = CreateStore(sharedStorage, time);
        var secondStore = CreateStore(sharedStorage, time);
        var issued = await firstStore.IssueAsync("jwt", CancellationToken.None);
        Assert.IsNotNull(issued);

        var results = await Task.WhenAll(
            firstStore.RotateAsync(issued.Token, "jwt", "next-a", CancellationToken.None),
            secondStore.RotateAsync(issued.Token, "jwt", "next-b", CancellationToken.None));

        Assert.IsTrue(results.All(result => result is not null));
        Assert.AreEqual(results[0]!.Token, results[1]!.Token);
        Assert.AreEqual(results[0]!.JwtId, results[1]!.JwtId);

        var descendant = await secondStore.RotateAsync(
            results[0]!.Token,
            results[0]!.JwtId,
            "grandchild",
            CancellationToken.None);
        Assert.IsNotNull(descendant);
    }

    [TestMethod]
    public async Task DuplicateStopsBeingIdempotentAfterBoundedGrace()
    {
        var time = new ManualTimeProvider();
        var store = CreateStore(new MemoryRefreshTokenStorage(), time);
        var issued = await store.IssueAsync("jwt", CancellationToken.None);
        Assert.IsNotNull(issued);
        Assert.IsNotNull(await store.RotateAsync(
            issued.Token,
            "jwt",
            "next",
            CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(3));

        Assert.IsNull(await store.RotateAsync(
            issued.Token,
            "jwt",
            "too-late",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task WrongJwtDoesNotConsumeRefreshToken()
    {
        var time = new ManualTimeProvider();
        var store = CreateStore(new MemoryRefreshTokenStorage(), time);
        var issued = await store.IssueAsync("jwt", CancellationToken.None);
        Assert.IsNotNull(issued);

        Assert.IsNull(await store.RotateAsync(
            issued.Token,
            "wrong",
            "next",
            CancellationToken.None));
        Assert.IsNotNull(await store.RotateAsync(
            issued.Token,
            "jwt",
            "next",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LogoutRevokesOutstandingRefreshToken()
    {
        var time = new ManualTimeProvider();
        var store = CreateStore(new MemoryRefreshTokenStorage(), time);
        var issued = await store.IssueAsync("jwt", CancellationToken.None);
        Assert.IsNotNull(issued);

        await store.RevokeAsync(issued.Token, CancellationToken.None);

        Assert.IsNull(await store.RotateAsync(
            issued.Token,
            "jwt",
            "next",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task LogoutOfRotatedTokenFamilyRevokesDescendant()
    {
        var time = new ManualTimeProvider();
        var store = CreateStore(new MemoryRefreshTokenStorage(), time);
        var first = await store.IssueAsync("jwt-1", CancellationToken.None);
        Assert.IsNotNull(first);
        var second = await store.RotateAsync(
            first.Token,
            "jwt-1",
            "jwt-2",
            CancellationToken.None);
        Assert.IsNotNull(second);

        await store.RevokeAsync(first.Token, CancellationToken.None);

        Assert.IsNull(await store.RotateAsync(
            second.Token,
            "jwt-2",
            "jwt-3",
            CancellationToken.None));
    }

    private static RefreshTokenStore CreateStore(
        IRefreshTokenStorage storage,
        TimeProvider timeProvider) =>
        new(
            storage,
            Options.Create(new TokenSecurityOptions
            {
                RefreshTokenDays = 30,
                RefreshTokenReuseGraceSeconds = 3
            }),
            timeProvider);

    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
