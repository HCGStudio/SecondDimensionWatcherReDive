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
            "IX_AnimationInfo_ReleaseLanguages_Gin",
            "IX_AnimationInfo_AnimationId",
            "UX_AnimationInfo_ActiveEpisodeRelease"
        };
        Assert.IsTrue(expected.All(indexes.Contains),
            $"Missing indexes: {string.Join(", ", expected.Except(indexes))}");
    }

    [TestMethod]
    public async Task Migration_DeduplicatesActiveReleasesBeforeCreatingUniqueIndex()
    {
        await using var migrationDatabase = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("sdw_migration_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await migrationDatabase.StartAsync();
        var fixture = new FileMappingRepositoryPostgreSqlTestFixture(
            migrationDatabase.GetConnectionString());

        var result = await fixture.MigrateDuplicateActiveReleasesAsync(CancellationToken.None);
        var indexes = await fixture.GetLibraryIndexNamesAsync(CancellationToken.None);

        Assert.HasCount(1, result.ActiveIds);
        Assert.AreEqual(result.ExpectedActiveId, result.ActiveIds[0]);
        Assert.AreEqual(1, result.DowngradedCandidateOperationCount);
        CollectionAssert.Contains(indexes.ToArray(), "IX_AnimationInfo_AnimationId");
        CollectionAssert.Contains(indexes.ToArray(), "UX_AnimationInfo_ActiveEpisodeRelease");
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
        Assert.HasCount(2, beforeCurrent);
        CollectionAssert.Contains(
            beforeCurrent.Select(mapping => mapping.VirtualPath).ToArray(),
            scenario.CanonicalPath);
        CollectionAssert.Contains(
            beforeCurrent.Select(mapping => mapping.VirtualPath).ToArray(),
            scenario.CanonicalSubtitlePath);

        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(applied.IsSuccess);
        Assert.AreEqual(ReleaseUpgradeStatus.Applied, applied.Operation!.Status);
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None));
        var activeCandidate = await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None);
        Assert.HasCount(2, activeCandidate);
        var activeVideo = activeCandidate.Single(mapping => mapping.VirtualPath == scenario.CanonicalPath);
        var activeSubtitle = activeCandidate.Single(mapping =>
            mapping.VirtualPath == scenario.CanonicalSubtitlePath);
        Assert.AreEqual("/store/new.mkv", activeVideo.PhysicalPath);
        Assert.AreEqual("/store/new.en.srt", activeSubtitle.PhysicalPath);
        var activeProgress = await Fixture.GetPlaybackProgressesAsync(
            scenario.UserId,
            CancellationToken.None);
        Assert.HasCount(1, activeProgress);
        Assert.AreEqual(scenario.Candidate.CandidateReleaseId, activeProgress[0].AnimationInfoId);
        Assert.AreEqual(scenario.CanonicalPath, activeProgress[0].VirtualPath);
        Assert.AreEqual(321d, activeProgress[0].PositionSeconds);
        var lateFailure = await Fixture.MarkUpgradeFailedAsync(operation.Id, CancellationToken.None);
        Assert.IsFalse(lateFailure.IsSuccess);
        Assert.AreEqual("invalid_state", lateFailure.Outcome);
        Assert.AreEqual(ReleaseUpgradeStatus.Applied, lateFailure.Operation!.Status);

        var rolledBack = await Fixture.RollbackUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(rolledBack.IsSuccess);
        Assert.AreEqual(ReleaseUpgradeStatus.RolledBack, rolledBack.Operation!.Status);
        var restored = await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None);
        Assert.HasCount(2, restored);
        CollectionAssert.Contains(
            restored.Select(mapping => mapping.VirtualPath).ToArray(),
            scenario.CanonicalPath);
        CollectionAssert.Contains(
            restored.Select(mapping => mapping.VirtualPath).ToArray(),
            scenario.CanonicalSubtitlePath);
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None));
        var restoredProgress = await Fixture.GetPlaybackProgressesAsync(
            scenario.UserId,
            CancellationToken.None);
        Assert.HasCount(1, restoredProgress);
        Assert.AreEqual(scenario.Candidate.CurrentReleaseId, restoredProgress[0].AnimationInfoId);
        Assert.AreEqual(scenario.CanonicalPath, restoredProgress[0].VirtualPath);
        Assert.AreEqual(321d, restoredProgress[0].PositionSeconds);
    }

    [TestMethod]
    public async Task IdentifiedAlternatives_KeepExactlyOneActiveRelease()
    {
        var activities = await Fixture.IdentifyCompetingReleasesAsync(CancellationToken.None);

        Assert.IsTrue(activities.FirstActive);
        Assert.IsFalse(activities.SecondActive);
    }

    [TestMethod]
    public async Task MovingActiveRelease_PromotesPreviousEpisodeSuccessor()
    {
        var activities = await Fixture.IdentifyCompetingReleasesAsync(
            CancellationToken.None,
            moveFirst: true);

        Assert.IsTrue(activities.FirstActive);
        Assert.AreEqual(2, activities.FirstEpisode);
        Assert.IsTrue(activities.SecondActive);
    }

    [TestMethod]
    public async Task DeidentifyingActiveRelease_PromotesPreviousEpisodeSuccessor()
    {
        var activities = await Fixture.IdentifyCompetingReleasesAsync(
            CancellationToken.None,
            deidentifyFirst: true);

        Assert.IsFalse(activities.FirstActive);
        Assert.IsTrue(activities.SecondActive);
    }

    [TestMethod]
    public async Task ConcurrentIdentification_ActivatesExactlyOneRelease()
    {
        var activities = await Fixture.IdentifyCompetingReleasesAsync(
            CancellationToken.None,
            concurrent: true);

        Assert.AreEqual(1, new[] { activities.FirstActive, activities.SecondActive }.Count(active => active));
    }

    [TestMethod]
    public async Task ReleaseUpgrade_RejectsMetadataDriftAfterOperationBegins()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);
        await Fixture.ChangeReleaseEpisodeAsync(
            scenario.Candidate.CandidateReleaseId,
            2,
            CancellationToken.None);

        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);

        Assert.IsFalse(applied.IsSuccess);
        Assert.AreEqual("release_changed", applied.Outcome);
        Assert.HasCount(2, await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None));
        Assert.HasCount(2, await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None));
    }

    [TestMethod]
    public async Task ReleaseUpgrade_RollbackRejectsInterveningMappingDrift()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);
        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(applied.IsSuccess);
        var remappedPath = "/Upgrade Show/Reviewed/Upgrade Show S01E01.MKV";
        await Fixture.RemapCandidatePlaybackAsync(
            scenario.Candidate.CandidateReleaseId,
            scenario.UserId,
            scenario.CanonicalPath,
            remappedPath,
            CancellationToken.None);

        var rolledBack = await Fixture.RollbackUpgradeAsync(operation.Id, CancellationToken.None);

        Assert.IsFalse(rolledBack.IsSuccess);
        Assert.AreEqual("mapping_changed", rolledBack.Outcome);
        var candidateMappings = await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None);
        CollectionAssert.Contains(
            candidateMappings.Select(mapping => mapping.VirtualPath).ToArray(),
            remappedPath);
        var progress = await Fixture.GetPlaybackProgressesAsync(
            scenario.UserId,
            CancellationToken.None);
        Assert.HasCount(1, progress);
        Assert.AreEqual(scenario.Candidate.CandidateReleaseId, progress[0].AnimationInfoId);
        Assert.AreEqual(remappedPath, progress[0].VirtualPath);
    }

    [TestMethod]
    public async Task ReleaseUpgrade_RejectsMappingsChangedAfterFileValidation()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);
        var expected = await Fixture.GetUpgradeActivationAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None);
        Assert.IsNotNull(expected);
        var candidateVideo = expected.CandidateMappings.Single(mapping =>
            mapping.PhysicalPath == "/store/new.mkv");
        await Fixture.ChangeMappingPhysicalPathAsync(
            scenario.Candidate.CandidateReleaseId,
            candidateVideo.VirtualPath,
            "/store/unvalidated.mkv",
            CancellationToken.None);

        var applied = await Fixture.ActivateUpgradeAsync(
            operation.Id,
            expected,
            CancellationToken.None);

        Assert.IsFalse(applied.IsSuccess);
        Assert.AreEqual("mapping_changed", applied.Outcome);
        Assert.HasCount(2, await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId, CancellationToken.None));
        var candidateMappings = await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId, CancellationToken.None);
        CollectionAssert.Contains(
            candidateMappings.Select(mapping => mapping.PhysicalPath).ToArray(),
            "/store/unvalidated.mkv");
    }

    [TestMethod]
    public async Task ReleaseUpgrade_DuplicateFileRolesRollbackDeterministically()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(
            CancellationToken.None,
            includeDuplicateVideoRole: true);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);

        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(applied.IsSuccess);
        var activeMappings = await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None);
        Assert.AreEqual(
            "/store/new.mkv",
            activeMappings.Single(mapping => mapping.VirtualPath == scenario.CanonicalPath).PhysicalPath);

        var rolledBack = await Fixture.RollbackUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(rolledBack.IsSuccess);
        Assert.HasCount(2, await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId,
            CancellationToken.None));
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task FailedReleaseUpgrade_CanBeClaimedAgain()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var first = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(first);
        var failed = await Fixture.MarkUpgradeFailedAsync(first.Id, CancellationToken.None);
        Assert.IsTrue(failed.IsSuccess);

        var retry = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);

        Assert.IsNotNull(retry);
        Assert.AreNotEqual(first.Id, retry.Id);
    }

    [TestMethod]
    public async Task CancellingTrackedUpgrade_TerminatesOperationAndAllowsRetry()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var downloadAttemptId = await Fixture.SetCandidateDownloadInProgressAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None);
        var first = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(first);
        Assert.AreEqual(ReleaseUpgradeStatus.Downloading, first.Status);

        var cancelled = await Fixture.CancelUpgradeCandidateAsync(
            scenario.Candidate.CandidateReleaseId,
            downloadAttemptId,
            CancellationToken.None);
        Assert.IsNotNull(cancelled);
        Assert.IsFalse(cancelled.IsDownloadTracked);
        var retry = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);

        Assert.IsNotNull(retry);
        Assert.AreNotEqual(first.Id, retry.Id);
    }

    [TestMethod]
    public async Task UpgradeCancellationIntent_PreventsActivationBeforeRemoteFinalize()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);
        Assert.AreEqual(ReleaseUpgradeStatus.Verifying, operation.Status);
        var expected = await Fixture.GetUpgradeActivationAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None);
        Assert.IsNotNull(expected);
        var cancellationAttemptId = Guid.NewGuid();

        var beganCancellation = await Fixture.BeginUpgradeCandidateCancellationAsync(
            scenario.Candidate.CandidateReleaseId,
            downloadAttemptId: null,
            cancellationAttemptId,
            CancellationToken.None);
        var applied = await Fixture.ActivateUpgradeAsync(
            operation.Id,
            expected,
            CancellationToken.None);
        var finalized = await Fixture.FinalizeUpgradeCandidateCancellationAsync(
            scenario.Candidate.CandidateReleaseId,
            downloadAttemptId: null,
            cancellationAttemptId,
            CancellationToken.None);
        var persisted = await Fixture.GetUpgradeOperationAsync(
            operation.Id,
            CancellationToken.None);

        Assert.IsTrue(beganCancellation);
        Assert.IsFalse(applied.IsSuccess);
        Assert.AreEqual("invalid_state", applied.Outcome);
        Assert.IsTrue(finalized);
        Assert.AreEqual(ReleaseUpgradeStatus.Failed, persisted.Status);
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None));
        Assert.HasCount(2, await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task AppliedUpgrade_RejectsStaleCancellationBeforeRemoteDeletion()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(CancellationToken.None);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);
        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(applied.IsSuccess);

        var beganCancellation = await Fixture.BeginUpgradeCandidateCancellationAsync(
            scenario.Candidate.CandidateReleaseId,
            downloadAttemptId: null,
            cancellationAttemptId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsFalse(beganCancellation);
        Assert.HasCount(2, await Fixture.GetMappingsAsync(
            scenario.Candidate.CandidateReleaseId,
            CancellationToken.None));
        Assert.IsEmpty(await Fixture.GetMappingsAsync(
            scenario.Candidate.CurrentReleaseId,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ReleaseUpgrade_MergesNewestPlaybackStateAcrossActivationAndRollback()
    {
        var scenario = await Fixture.SeedUpgradeScenarioAsync(
            CancellationToken.None,
            includeCandidateProgress: true);
        var operation = await Fixture.BeginUpgradeAsync(scenario.Candidate, CancellationToken.None);
        Assert.IsNotNull(operation);

        var applied = await Fixture.ActivateUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(applied.IsSuccess);
        var activeProgress = await Fixture.GetPlaybackProgressesAsync(
            scenario.UserId,
            CancellationToken.None);
        Assert.HasCount(1, activeProgress);
        Assert.AreEqual(scenario.Candidate.CandidateReleaseId, activeProgress[0].AnimationInfoId);
        Assert.IsTrue(activeProgress[0].IsWatched);
        Assert.IsNotNull(activeProgress[0].WatchedAt);
        Assert.AreEqual(1200d, activeProgress[0].PositionSeconds);

        var rolledBack = await Fixture.RollbackUpgradeAsync(operation.Id, CancellationToken.None);
        Assert.IsTrue(rolledBack.IsSuccess);
        var restoredProgress = await Fixture.GetPlaybackProgressesAsync(
            scenario.UserId,
            CancellationToken.None);
        Assert.HasCount(1, restoredProgress);
        Assert.AreEqual(scenario.Candidate.CurrentReleaseId, restoredProgress[0].AnimationInfoId);
        Assert.IsTrue(restoredProgress[0].IsWatched);
        Assert.IsNotNull(restoredProgress[0].WatchedAt);
        Assert.AreEqual(1200d, restoredProgress[0].PositionSeconds);
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
