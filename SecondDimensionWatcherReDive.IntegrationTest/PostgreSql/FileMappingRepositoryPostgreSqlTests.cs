using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.IntegrationTest.PostgreSql;

/// <summary>
/// Exercises the PostgreSQL-only repository surface against a migrated, disposable database.
/// Testcontainers owns container cleanup even when a test fails or the run is cancelled.
/// </summary>
[TestClass]
public sealed class FileMappingRepositoryPostgreSqlTests
{
    private static readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sdw_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private static FileMappingRepositoryPostgreSqlTestFixture Fixture = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Fixture = new FileMappingRepositoryPostgreSqlTestFixture(Database.GetConnectionString());
        await Fixture.InitializeAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        await Fixture.ResetAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task PrefixQuery_EscapesLikeWildcards_AndRootQueryUsesPostgreSqlRawSql()
    {
        var infoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        await Fixture.AddRangeAsync([
            Mapping(infoId, "/shows/100%_real/episode.mkv"),
            Mapping(infoId, "/shows/100xxreal/other.mkv")
        ], CancellationToken.None);

        var matches = await Fixture.GetByVirtualPathPrefixAsync(
            "/shows/100%_real", CancellationToken.None);
        var roots = await Fixture.GetRootEntriesAsync(CancellationToken.None);

        Assert.HasCount(1, matches);
        Assert.AreEqual("/shows/100%_real/episode.mkv", matches[0].VirtualPath);
        Assert.HasCount(1, roots);
        Assert.AreEqual("shows", roots[0].Name);
        Assert.IsTrue(roots[0].IsDirectory);
    }

    [TestMethod]
    public async Task ConcurrentWriters_AreSerialized_AndFailedTransactionRollsBack()
    {
        var firstInfoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        var secondInfoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        const string CollidingPath = "/anime/shared.mkv";

        static async Task<bool> WriteAsync(FileMappingRepositoryPostgreSqlTestFixture fixture,
            FileMapping mapping)
        {
            try
            {
                await fixture.AddRangeAsync([mapping], CancellationToken.None);
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            WriteAsync(Fixture, Mapping(firstInfoId, CollidingPath)),
            WriteAsync(Fixture, Mapping(secondInfoId, CollidingPath)));

        Assert.AreEqual(1, results.Count(result => result));
        Assert.AreEqual(1, await Fixture.GetMappingCountAsync(CancellationToken.None));
        var versions = await Fixture.GetAnimationInfoStateVersionsAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new long[] { 0, 1 }, versions);
    }

    [TestMethod]
    public async Task AuthenticationClaim_AcrossIndependentContexts_HasExactlyOneWinner()
    {
        var candidates = Enumerable.Range(0, 16)
            .Select(index => (Hash: $"hash-{index}", ClaimId: Guid.NewGuid()))
            .ToArray();

        var results = await Task.WhenAll(candidates.Select(candidate =>
            Fixture.TryClaimPasswordAsync(candidate.Hash, candidate.ClaimId, CancellationToken.None)));

        Assert.AreEqual(1, results.Count(result => result));
        var winner = candidates[Array.FindIndex(results, result => result)];
        Assert.AreEqual(winner.Hash, await Fixture.GetPasswordHashAsync(CancellationToken.None));
        Assert.IsTrue(await Fixture.TryClaimPasswordAsync(
            winner.Hash, winner.ClaimId, CancellationToken.None));
    }

    private static FileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/" + Guid.NewGuid(), "local");
}
