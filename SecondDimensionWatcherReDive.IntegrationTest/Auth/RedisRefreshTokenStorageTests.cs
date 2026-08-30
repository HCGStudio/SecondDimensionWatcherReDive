using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.IntegrationTest.Auth;

[TestClass]
public sealed class RedisRefreshTokenStorageTests
{
    private const ushort RedisPort = 6379;
    private static readonly IContainer Valkey = new ContainerBuilder("valkey/valkey:9-alpine")
        .WithPortBinding(RedisPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(RedisPort))
        .Build();

    private static RedisConnectionProvider FirstConnection = null!;
    private static RedisConnectionProvider SecondConnection = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Valkey.StartAsync();
        var connectionString = $"{Valkey.Hostname}:{Valkey.GetMappedPublicPort(RedisPort)}";
        FirstConnection = new RedisConnectionProvider(connectionString);
        SecondConnection = new RedisConnectionProvider(connectionString);
    }

    [ClassCleanup]
    public static async Task CleanupAsync()
    {
        await FirstConnection.DisposeAsync();
        await SecondConnection.DisposeAsync();
        await Valkey.DisposeAsync();
    }

    [TestMethod]
    public async Task AtomicRotationAcrossConnectionsReturnsOneReplacement()
    {
        var instanceName = $"integration:{Guid.NewGuid():N}:";
        var options = Options.Create(new TokenSecurityOptions
        {
            RefreshTokenDays = 1,
            RefreshTokenReuseGraceSeconds = 3
        });
        var first = new RefreshTokenStore(
            new RedisRefreshTokenStorage(FirstConnection, instanceName),
            options,
            TimeProvider.System);
        var second = new RefreshTokenStore(
            new RedisRefreshTokenStorage(SecondConnection, instanceName),
            options,
            TimeProvider.System);
        var issued = await first.IssueAsync("jwt", CancellationToken.None);
        Assert.IsNotNull(issued);

        var rotations = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            (index % 2 == 0 ? first : second).RotateAsync(
                issued.Token,
                "jwt",
                $"replacement-{index}",
                CancellationToken.None)));

        Assert.IsTrue(rotations.All(rotation => rotation is not null));
        Assert.AreEqual(1, rotations.Select(rotation => rotation!.Token).Distinct().Count());
        Assert.AreEqual(1, rotations.Select(rotation => rotation!.JwtId).Distinct().Count());

        var replacement = rotations[0]!;
        Assert.IsNotNull(await second.RotateAsync(
            replacement.Token,
            replacement.JwtId,
            "grandchild",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ReplayOutsideConfiguredGraceRevokesDescendant()
    {
        var instanceName = $"integration:{Guid.NewGuid():N}:";
        var options = Options.Create(new TokenSecurityOptions
        {
            RefreshTokenDays = 1,
            RefreshTokenReuseGraceSeconds = 0
        });
        var first = new RefreshTokenStore(
            new RedisRefreshTokenStorage(FirstConnection, instanceName),
            options,
            TimeProvider.System);
        var second = new RefreshTokenStore(
            new RedisRefreshTokenStorage(SecondConnection, instanceName),
            options,
            TimeProvider.System);
        var issued = await first.IssueAsync("jwt", CancellationToken.None);
        Assert.IsNotNull(issued);
        var replacement = await first.RotateAsync(
            issued.Token,
            "jwt",
            "replacement",
            CancellationToken.None);
        Assert.IsNotNull(replacement);

        Assert.IsNull(await second.RotateAsync(
            issued.Token,
            "jwt",
            "replay",
            CancellationToken.None));
        Assert.IsNull(await first.RotateAsync(
            replacement.Token,
            replacement.JwtId,
            "descendant",
            CancellationToken.None));
    }
}
