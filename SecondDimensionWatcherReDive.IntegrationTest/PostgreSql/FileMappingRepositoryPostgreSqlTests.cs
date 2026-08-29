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
    public async Task StableReleaseIdentity_ConcurrentRepositoryInserts_PersistExactlyOnce()
    {
        var identity = "torrent:" + Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(
            Fixture.TryAddReleaseAsync(identity, CancellationToken.None),
            Fixture.TryAddReleaseAsync(identity, CancellationToken.None));

        Assert.AreEqual(1, results.Count(result => result));
        Assert.AreEqual(1, await Fixture.CountReleaseIdentityAsync(identity, CancellationToken.None));
    }

    [TestMethod]
    public async Task Search_ComposesImportPathAndWatchFilters_AndCursorIgnoresConcurrentInsert()
    {
        var scenario = await Fixture.SeedLibraryScenarioAsync(CancellationToken.None);
        var filtered = await Fixture.SearchAsync(new LibrarySearchRequest(
                "Attack", 1, 2, null, "2160p", "AV1", "ja",
                LibraryDownloadState.Downloaded,
                LibraryWatchState.InProgress,
                "Imported",
                LibrarySourceKind.MediaLibraryImport,
                LibrarySearchSort.ScoreDescending,
                null,
                20,
                scenario.UserId),
            CancellationToken.None);

        Assert.HasCount(1, filtered.Items);
        Assert.AreEqual(scenario.ImportedReleaseId, filtered.Items[0].AnimationInfoId);
        Assert.IsTrue(filtered.Items[0].IsMediaLibraryImport);

        var firstPage = await Fixture.SearchAsync(AnySearch(scenario.UserId, null, 2), CancellationToken.None);
        Assert.IsNotNull(firstPage.NextCursor);
        var insertedId = await Fixture.InsertConcurrentSearchReleaseAsync(CancellationToken.None);
        var secondPage = await Fixture.SearchAsync(
            AnySearch(scenario.UserId, firstPage.NextCursor, 2),
            CancellationToken.None);

        Assert.IsFalse(secondPage.Items.Any(item => item.AnimationInfoId == insertedId));
        Assert.IsFalse(firstPage.Items.Select(item => item.AnimationInfoId)
            .Intersect(secondPage.Items.Select(item => item.AnimationInfoId)).Any());
    }

    [TestMethod]
    public async Task Integrity_ReportsMissingDuplicateUnidentifiedAndExplainableUpgrade()
    {
        await Fixture.SeedLibraryScenarioAsync(CancellationToken.None);

        var summaries = await Fixture.GetIntegrityAsync(CancellationToken.None);

        Assert.HasCount(1, summaries);
        var summary = summaries[0];
        CollectionAssert.AreEqual(new[] { 3 }, summary.MissingEpisodes.ToArray());
        Assert.HasCount(1, summary.DuplicateEpisodes);
        Assert.AreEqual(1, summary.DuplicateEpisodes[0].Episode);
        Assert.AreEqual(1, summary.UnidentifiedReleaseCount);
        Assert.HasCount(1, summary.UpgradeCandidates);
        CollectionAssert.Contains(
            summary.UpgradeCandidates[0].ScoreReasons.ToArray(),
            "resolution:2160p:+400");
    }

    [TestMethod]
    public async Task Migration_CreatesReleaseUniquenessAndSearchIndexes()
    {
        var indexes = await Fixture.GetLibraryIndexNamesAsync(CancellationToken.None);

        var expected = new[]
        {
            "UX_AnimationInfo_ReleaseIdentity",
            "IX_AnimationInfo_Title_Trgm",
            "IX_Animations_Name_Trgm",
            "IX_Animations_OriginalName_Trgm",
            "IX_AnimationGroups_Name_Trgm",
            "IX_FileMappings_VirtualPath_Trgm",
            "IX_AnimationInfo_ReleaseLanguages_Gin"
        };
        Assert.IsTrue(expected.All(indexes.Contains),
            $"Missing indexes: {string.Join(", ", expected.Except(indexes))}");
    }

    [TestMethod]
    public async Task UpgradeRace_ClaimsOnce_AtomicallySwapsMappings_AndRollsBack()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var claims = await Task.WhenAll(
            Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None),
            Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None));
        var operation = claims.Single(claim => claim is not null)!;
        var recoverableCandidates = await Fixture.GetReadyUpgradeCandidateIdsAsync(CancellationToken.None);
        CollectionAssert.Contains(recoverableCandidates.ToArray(), scenario.Candidate.CandidateReleaseId);

        var beforeCurrent = await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None);
        Assert.HasCount(1, beforeCurrent);
        Assert.AreEqual(scenario.CanonicalPath, beforeCurrent[0].VirtualPath);

        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(applied.IsSuccess);
        Assert.AreEqual(ReleaseUpgradeStatus.Applied, applied.Operation!.Status);
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None));
        var activeCandidate = await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None);
        Assert.HasCount(1, activeCandidate);
        Assert.AreEqual(scenario.CanonicalPath, activeCandidate[0].VirtualPath);

        var rolledBack = await Fixture.RollbackUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(rolledBack.IsSuccess);
        Assert.AreEqual(ReleaseUpgradeStatus.RolledBack, rolledBack.Operation!.Status);
        var restored = await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None);
        Assert.HasCount(1, restored);
        Assert.AreEqual(scenario.CanonicalPath, restored[0].VirtualPath);
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None));
    }

    private static FileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/" + Guid.NewGuid(), "local");

    private static LibrarySearchRequest AnySearch(Guid userId, string? cursor, int take) =>
        new(null, null, null, null, null, null, null,
            LibraryDownloadState.Any,
            LibraryWatchState.Any,
            null,
            LibrarySourceKind.Any,
            LibrarySearchSort.PublishedDescending,
            cursor,
            take,
            userId);
}
