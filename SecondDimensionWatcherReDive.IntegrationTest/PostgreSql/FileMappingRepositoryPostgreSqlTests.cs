using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    public async Task PrefixQuery_EscapesLikeWildcards_AndRootQueryUsesHierarchyIndex()
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
    public void Migration_BackfillsMappingsCreatedBeforeTheReadModel()
    {
        Assert.IsTrue(Fixture.BackfillVerified);
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
    public async Task HierarchyQuery_ReturnsOnlyImmediateChildren_AndExactDirectories()
    {
        var infoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        await Fixture.AddRangeAsync([
            Mapping(infoId, "/shows/a/episode.mkv"),
            Mapping(infoId, "/shows/a/extras/trailer.mkv"),
            Mapping(infoId, "/shows/b/episode.mkv")
        ], CancellationToken.None);

        var children = await Fixture.GetImmediateChildrenAsync(
            "/shows/a",
            CancellationToken.None);
        var directory = await Fixture.FindFileSystemEntryAsync(
            "/shows/a/extras",
            CancellationToken.None);

        Assert.HasCount(2, children);
        Assert.IsTrue(children.Any(entry => entry.Name == "episode.mkv" && !entry.IsDirectory));
        Assert.IsTrue(children.Any(entry => entry.Name == "extras" && entry.IsDirectory));
        Assert.IsNotNull(directory);
        Assert.IsTrue(directory.IsDirectory);
    }

    [TestMethod]
    public async Task ReplaceAndRemove_AtomicallyPruneOnlyOrphanedDirectories()
    {
        var firstId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        var secondId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        await Fixture.AddRangeAsync([
            Mapping(firstId, "/anime/shared/first.mkv"),
            Mapping(firstId, "/anime/old/extra.srt")
        ], CancellationToken.None);
        await Fixture.AddRangeAsync([
            Mapping(secondId, "/anime/shared/second.mkv")
        ], CancellationToken.None);

        var replaced = await Fixture.ReplaceAsync(firstId, [
            Mapping(firstId, "/anime/new/first.mkv")
        ], CancellationToken.None);

        Assert.IsTrue(replaced);
        Assert.IsNull(await Fixture.FindFileSystemEntryAsync("/anime/old", CancellationToken.None));
        Assert.IsNotNull(await Fixture.FindFileSystemEntryAsync("/anime/new", CancellationToken.None));
        Assert.IsNotNull(await Fixture.FindFileSystemEntryAsync("/anime/shared", CancellationToken.None));

        await Fixture.RemoveAsync(secondId, CancellationToken.None);
        Assert.IsNull(await Fixture.FindFileSystemEntryAsync("/anime/shared", CancellationToken.None));
        Assert.IsNotNull(await Fixture.FindFileSystemEntryAsync("/anime/new", CancellationToken.None));

        await Fixture.RemoveAsync(firstId, CancellationToken.None);
        Assert.AreEqual(0, await Fixture.GetHierarchyEntryCountAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task DirectVirtualPathUpdate_IsRejectedToProtectHierarchyConsistency()
    {
        var infoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        var mapping = Mapping(infoId, "/protected/original.mkv");
        await Fixture.AddRangeAsync([mapping], CancellationToken.None);

        await Assert.ThrowsExactlyAsync<PostgresException>(() =>
            Fixture.UpdateVirtualPathDirectlyAsync(
                mapping.Id,
                "/protected/renamed.mkv",
                CancellationToken.None));

        Assert.IsNotNull(await Fixture.FindFileSystemEntryAsync(
            "/protected/original.mkv",
            CancellationToken.None));
        Assert.IsNull(await Fixture.FindFileSystemEntryAsync(
            "/protected/renamed.mkv",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task AnimationCatalog_UsesLeanStableCursorPages_AndLazyEpisodePages()
    {
        var publishedAt = new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);
        await Fixture.SeedCatalogReleaseAsync("100", publishedAt, 1, CancellationToken.None);
        await Fixture.SeedCatalogReleaseAsync("200", publishedAt, 1, CancellationToken.None);
        await Fixture.SeedCatalogReleaseAsync("300", publishedAt, 1, CancellationToken.None);
        await Fixture.SeedCatalogReleaseAsync("300", publishedAt.AddMinutes(-1), 2, CancellationToken.None);
        await Fixture.SeedCatalogReleaseAsync(null, publishedAt, null, CancellationToken.None);
        await Fixture.SeedCatalogReleaseAsync(null, publishedAt, null, CancellationToken.None);

        var (first, sql) = await Fixture.GetCatalogPageWithSqlAsync(
            cursor: null,
            take: 2,
            CancellationToken.None);
        var (second, _) = await Fixture.GetCatalogPageWithSqlAsync(
            first.NextCursor,
            take: 2,
            CancellationToken.None);
        var catalogIds = first.Items.Concat(second.Items).Select(item => item.TmdbId).ToArray();

        Assert.HasCount(3, catalogIds);
        Assert.AreEqual(3, catalogIds.Distinct().Count());
        Assert.IsFalse(sql.Contains("CachedDownloadData", StringComparison.Ordinal));
        StringAssert.Contains(sql, "LIMIT");

        var episodes = await Fixture.GetEpisodesPageAsync(
            "300",
            cursor: null,
            take: 1,
            CancellationToken.None);
        Assert.IsNotNull(episodes);
        Assert.HasCount(1, episodes.Episodes);
        Assert.IsNotNull(episodes.NextCursor);
        var episodeTail = await Fixture.GetEpisodesPageAsync(
            "300",
            episodes.NextCursor,
            take: 1,
            CancellationToken.None);
        Assert.IsNotNull(episodeTail);
        Assert.HasCount(1, episodeTail.Episodes);
        Assert.AreNotEqual(episodes.Episodes[0].Id, episodeTail.Episodes[0].Id);

        var uncategorized = await Fixture.GetUncategorizedPageAsync(
            cursor: null,
            take: 1,
            CancellationToken.None);
        var uncategorizedTail = await Fixture.GetUncategorizedPageAsync(
            uncategorized.NextCursor,
            take: 1,
            CancellationToken.None);
        Assert.HasCount(1, uncategorized.Items);
        Assert.HasCount(1, uncategorizedTail.Items);
        Assert.AreNotEqual(uncategorized.Items[0].Id, uncategorizedTail.Items[0].Id);
    }

    private static FileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/" + Guid.NewGuid(), "local");
}
