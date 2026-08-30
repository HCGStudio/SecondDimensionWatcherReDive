using Microsoft.EntityFrameworkCore;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
using SecondDimensionWatcherReDive.Utils.FileStore;
using Testcontainers.PostgreSql;
using Models = SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.IntegrationTest.PostgreSql;

[TestClass]
public sealed class LogicalDataTransferPostgreSqlTests
{
    private static readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sdw_transfer_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private static DbContextOptions<Models.ApplicationContext> Options = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Options = new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(Database.GetConnectionString())
            .Options;
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetAsync()
    {
        await using var context = new Models.ApplicationContext(Options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    [TestMethod]
    public async Task FeedsPoliciesAndRulesRoundTripWithExplicitConflictStrategies()
    {
        LogicalDataBundle bundle;
        await using (var source = new Models.ApplicationContext(Options))
        {
            var feed = new Models.Feed
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/feed.xml",
                Name = "Example",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
            };
            var animation = new Models.Animation
            {
                Id = Guid.NewGuid(),
                TmdbId = "tv:123",
                Name = "Example Show",
                OriginalName = "Example Show",
                PosterPath = "/poster.jpg"
            };
            source.AddRange(
                feed,
                animation,
                new Models.SubscriptionAutomationPolicy
                {
                    FeedId = feed.Id,
                    Feed = feed,
                    SubtitleGroups = ["Group"],
                    Resolutions = ["1080p"],
                    Codecs = ["HEVC"],
                    Languages = ["zh-Hans"],
                    ExcludedKeywords = ["batch"],
                    Mode = SubscriptionAutomationMode.ManualConfirm,
                    CreatedAt = feed.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                new Models.FileNameRegexRule
                {
                    Id = Guid.NewGuid(),
                    AnimationId = animation.Id,
                    Pattern = @"E(?<episode>\d+)",
                    Description = "episode",
                    CreatedAt = DateTimeOffset.UtcNow
                });
            await source.SaveChangesAsync();
            bundle = await Repository(source).ExportAsync(
                LogicalDataCategory.Feeds |
                LogicalDataCategory.AutomationPolicies |
                LogicalDataCategory.FileNameRules,
                Guid.Empty,
                "1.0.0",
                CancellationToken.None);

            await source.SubscriptionAutomationPolicies.ExecuteDeleteAsync();
            await source.FileNameRegexRules.ExecuteDeleteAsync();
            await source.Feeds.ExecuteDeleteAsync();
            await source.Animations.ExecuteDeleteAsync();
        }

        await using var target = new Models.ApplicationContext(Options);
        var repository = Repository(target);
        var first = await repository.ImportAsync(
            bundle,
            LogicalImportConflictStrategy.Skip,
            Guid.Empty,
            CancellationToken.None);
        Assert.AreEqual(3, first.Added);
        Assert.AreEqual(1, await target.Feeds.CountAsync());
        Assert.AreEqual(1, await target.SubscriptionAutomationPolicies.CountAsync());
        Assert.AreEqual(1, await target.FileNameRegexRules.CountAsync());

        var repeated = await repository.ImportAsync(
            bundle,
            LogicalImportConflictStrategy.Skip,
            Guid.Empty,
            CancellationToken.None);
        Assert.AreEqual(3, repeated.Skipped);
        Assert.AreEqual(1, repeated.Conflicts);

        var changed = bundle with
        {
            Feeds = [bundle.Feeds[0] with { Name = "Renamed" }],
            AutomationPolicies =
            [bundle.AutomationPolicies[0] with { Mode = SubscriptionAutomationMode.AutoDownload }],
            FileNameRules = [bundle.FileNameRules[0] with { Description = "updated" }]
        };
        var overwritten = await repository.ImportAsync(
            changed,
            LogicalImportConflictStrategy.Overwrite,
            Guid.Empty,
            CancellationToken.None);
        Assert.AreEqual(3, overwritten.Updated);
        Assert.AreEqual("Renamed", (await target.Feeds.SingleAsync()).Name);
        Assert.AreEqual(SubscriptionAutomationMode.AutoDownload,
            (await target.SubscriptionAutomationPolicies.SingleAsync()).Mode);
        Assert.AreEqual("updated", (await target.FileNameRegexRules.SingleAsync()).Description);
    }

    [TestMethod]
    public async Task MetadataAndPlaybackUseStableReleaseAndVirtualPathKeys()
    {
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        const string DownloadUrl = "https://example.com/release.torrent";
        const string VirtualPath = "/Example/Group/Example S01E02.mkv";
        LogicalDataBundle bundle;

        await using (var source = new Models.ApplicationContext(Options))
        {
            var animation = Animation("tv:456", "Correct Show");
            var group = new Models.AnimationGroup { Id = Guid.NewGuid(), Name = "Correct Group" };
            var info = Release(Guid.NewGuid(), DownloadUrl, publishedAt);
            info.Animation = animation;
            info.Group = group;
            info.Description = "corrected description";
            info.Season = 1;
            info.Episode = 2;
            info.MetadataStatus = MetadataReviewStatus.Reviewed;
            info.MetadataReviewedAt = DateTimeOffset.UtcNow;
            info.StateVersion = 1;
            var mapping = Mapping(info.Id, VirtualPath);
            source.AddRange(animation, group, info, mapping);
            await source.SaveChangesAsync();

            var operation = new Models.MetadataReviewOperation
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = info.Id,
                AnimationInfo = info,
                State = MetadataReviewOperationState.Applied,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                BaseVersion = 0,
                ProposedAnimationTmdbId = animation.TmdbId,
                ProposedAnimationName = animation.Name,
                ProposedAnimationOriginalName = animation.OriginalName,
                ProposedAnimationPosterPath = animation.PosterPath,
                ProposedDescription = info.Description,
                ProposedSeason = info.Season,
                ProposedEpisode = info.Episode,
                ProposedGroupName = group.Name,
                AppliedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                AppliedVersion = 1,
                PreviousDescription = "uncorrected",
                PreviousMetadataStatus = MetadataReviewStatus.LowConfidence,
                PreviousIsAiProcessed = true,
                PreviousAiRetryCount = 0
            };
            info.CurrentMetadataReviewOperationId = operation.Id;
            source.AddRange(
                operation,
                new Models.MetadataReviewOperation
                {
                    Id = Guid.NewGuid(),
                    AnimationInfoId = info.Id,
                    AnimationInfo = info,
                    State = MetadataReviewOperationState.Applied,
                    CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                    BaseVersion = 0,
                    AppliedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    AppliedVersion = 0
                },
                new Models.PlaybackProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = Guid.Empty,
                    AnimationInfoId = info.Id,
                    VirtualPath = VirtualPath,
                    PositionSeconds = 600,
                    DurationSeconds = 1_440,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            await source.SaveChangesAsync();
            await source.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO "PlaybackPreferences"
                     ("UserId", "SubtitleLanguage", "AutoPlayNext", "UpdatedAt")
                 VALUES ({Guid.Empty}, {"zh-Hans"}, {false}, {DateTimeOffset.UtcNow})
                 """);
            Assert.AreEqual(1, await source.PlaybackPreferences.AsNoTracking().CountAsync());

            bundle = await Repository(source).ExportAsync(
                LogicalDataCategory.MetadataCorrections | LogicalDataCategory.Playback,
                Guid.Empty,
                "1.0.0",
                CancellationToken.None);
            Assert.IsNotNull(bundle.PlaybackPreferences);
            Assert.AreEqual(1, bundle.MetadataCorrections.Count);
            Assert.AreEqual(operation.Id, bundle.MetadataCorrections[0].OperationId);

            await source.MetadataReviewOperations.ExecuteDeleteAsync();
            await source.PlaybackProgresses.ExecuteDeleteAsync();
            await source.PlaybackPreferences.ExecuteDeleteAsync();
            await source.FileMappings.ExecuteDeleteAsync();
            await source.AnimationInfo.ExecuteDeleteAsync();
            await source.Animations.ExecuteDeleteAsync();
            await source.AnimationGroups.ExecuteDeleteAsync();
        }

        var targetInfoId = Guid.NewGuid();
        await using var target = new Models.ApplicationContext(Options);
        var targetInfo = Release(targetInfoId, DownloadUrl, publishedAt);
        targetInfo.Description = "uncorrected";
        target.AddRange(targetInfo, Mapping(targetInfoId, VirtualPath));
        await target.SaveChangesAsync();

        var result = await Repository(target).ImportAsync(
            bundle,
            LogicalImportConflictStrategy.Skip,
            Guid.Empty,
            CancellationToken.None);

        Assert.AreEqual(0, result.Skipped, string.Join(Environment.NewLine, result.Messages));
        Assert.AreEqual(3, result.Added);
        var importedInfo = await target.AnimationInfo
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .SingleAsync();
        Assert.AreEqual("corrected description", importedInfo.Description);
        Assert.AreEqual("tv:456", importedInfo.Animation!.TmdbId);
        Assert.AreEqual("Correct Group", importedInfo.Group!.Name);
        Assert.IsNotNull(importedInfo.CurrentMetadataReviewOperationId);
        var progress = await target.PlaybackProgresses.SingleAsync();
        Assert.AreEqual(targetInfoId, progress.AnimationInfoId);
        Assert.AreEqual(600, progress.PositionSeconds);
        var preferences = await target.PlaybackPreferences.SingleAsync();
        Assert.AreEqual(Guid.Empty, preferences.UserId);
        Assert.IsFalse(preferences.AutoPlayNext);
    }

    [TestMethod]
    public async Task MetadataImportTransitionsMappingsPlaybackAndRemainsUndoable()
    {
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var operationId = Guid.NewGuid();
        var infoId = Guid.NewGuid();
        const string DownloadUrl = "https://example.com/remapped-release.torrent";
        const string PreviousPath = "/Old Show/Old Group/Old Show S01E02.mkv";
        const string ProposedPath = "/Correct Show/Correct Group/Correct Show S02E05.mkv";
        const string PhysicalPath = "/target-media/release.mkv";

        await using (var seed = new Models.ApplicationContext(Options))
        {
            var oldAnimation = Animation("tv:old", "Old Show");
            var oldGroup = new Models.AnimationGroup { Id = Guid.NewGuid(), Name = "Old Group" };
            var info = Release(infoId, DownloadUrl, publishedAt);
            info.Animation = oldAnimation;
            info.Group = oldGroup;
            info.Season = 1;
            info.Episode = 2;
            info.IsDownloadFinished = true;
            info.FileStore = "local";
            info.StorePath = "/target-media";
            info.StateVersion = 7;
            seed.AddRange(
                oldAnimation,
                oldGroup,
                info,
                new Models.FileMapping
                {
                    Id = Guid.NewGuid(),
                    AnimationInfoId = infoId,
                    VirtualPath = PreviousPath,
                    PhysicalPath = PhysicalPath,
                    FileStore = "local"
                },
                new Models.PlaybackProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = Guid.Empty,
                    AnimationInfoId = infoId,
                    VirtualPath = PreviousPath,
                    PositionSeconds = 120,
                    DurationSeconds = 1_440,
                    UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
                });
            await seed.SaveChangesAsync();
        }

        var bundle = new LogicalDataBundle(
            1,
            DateTimeOffset.UtcNow,
            "1.0.0",
            LogicalDataCategory.MetadataCorrections | LogicalDataCategory.Playback,
            [],
            [],
            [],
            [
                new LogicalMetadataCorrection(
                    operationId,
                    DownloadUrl,
                    "[Group] Example - 02",
                    publishedAt,
                    "tv:correct",
                    "Correct Show",
                    "Correct Show",
                    "/correct.jpg",
                    "corrected description",
                    2,
                    5,
                    "Correct Group",
                    DateTimeOffset.UtcNow)
            ],
            [
                new LogicalPlaybackProgress(
                    ProposedPath,
                    600,
                    1_440,
                    false,
                    DateTimeOffset.UtcNow,
                    null)
            ],
            null);
        var mapper = new Mock<IFileMapper>(MockBehavior.Strict);
        mapper.Setup(candidate => candidate.PreviewDownloadAsync(
                It.Is<AnimationInfo>(info =>
                    info.Id == infoId &&
                    info.Animation != null && info.Animation.TmdbId == "tv:correct" &&
                    info.Group != null && info.Group.Name == "Correct Group" &&
                    info.Season == 2 && info.Episode == 5),
                CancellationToken.None))
            .ReturnsAsync(new FileMappingPreview(
                [
                    new FileMapping(
                        Guid.NewGuid(),
                        infoId,
                        ProposedPath,
                        PhysicalPath,
                        "local")
                ],
                []));

        await using (var importing = new Models.ApplicationContext(Options))
        {
            var result = await Repository(importing, mapper.Object).ImportAsync(
                bundle,
                LogicalImportConflictStrategy.Overwrite,
                Guid.Empty,
                CancellationToken.None);
            Assert.AreEqual(1, result.Added);
            Assert.AreEqual(1, result.Updated);
        }

        await using (var verification = new Models.ApplicationContext(Options))
        {
            var info = await verification.AnimationInfo
                .Include(candidate => candidate.Animation)
                .Include(candidate => candidate.Group)
                .SingleAsync();
            Assert.AreEqual(8, info.StateVersion);
            Assert.AreEqual(operationId, info.CurrentMetadataReviewOperationId);
            Assert.AreEqual("tv:correct", info.Animation!.TmdbId);
            Assert.AreEqual("Correct Group", info.Group!.Name);

            var mapping = await verification.FileMappings.SingleAsync();
            Assert.AreEqual(ProposedPath, mapping.VirtualPath);
            Assert.AreEqual(PhysicalPath, mapping.PhysicalPath);
            var snapshots = await verification.MetadataReviewMappingSnapshots
                .OrderBy(snapshot => snapshot.Kind)
                .ToListAsync();
            Assert.AreEqual(2, snapshots.Count);
            Assert.AreEqual(PreviousPath,
                snapshots.Single(snapshot => snapshot.Kind == MetadataReviewMappingKind.Previous).VirtualPath);
            Assert.AreEqual(ProposedPath,
                snapshots.Single(snapshot => snapshot.Kind == MetadataReviewMappingKind.Proposed).VirtualPath);
            Assert.IsTrue(snapshots.All(snapshot => snapshot.PhysicalPath == PhysicalPath));
            var progress = await verification.PlaybackProgresses.SingleAsync();
            Assert.AreEqual(ProposedPath, progress.VirtualPath);
            Assert.AreEqual(600, progress.PositionSeconds);
        }

        await using (var undoContext = new Models.ApplicationContext(Options))
        {
            var undone = await new MetadataReviewRepository(undoContext, Options).UndoAsync(
                operationId,
                8,
                CancellationToken.None);
            Assert.AreEqual(MetadataReviewMutationOutcome.Success, undone.Outcome);
        }

        await using (var verification = new Models.ApplicationContext(Options))
        {
            var info = await verification.AnimationInfo
                .Include(candidate => candidate.Animation)
                .Include(candidate => candidate.Group)
                .SingleAsync();
            Assert.AreEqual(9, info.StateVersion);
            Assert.AreEqual("tv:old", info.Animation!.TmdbId);
            Assert.AreEqual("Old Group", info.Group!.Name);
            Assert.AreEqual(PreviousPath, (await verification.FileMappings.SingleAsync()).VirtualPath);
            var progress = await verification.PlaybackProgresses.SingleAsync();
            Assert.AreEqual(PreviousPath, progress.VirtualPath);
            Assert.AreEqual(600, progress.PositionSeconds);
        }
    }

    [TestMethod]
    public async Task UnavailableMetadataMappingPlanRollsBackWholeImport()
    {
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var infoId = Guid.NewGuid();
        const string DownloadUrl = "https://example.com/unavailable-mapping.torrent";
        const string PreviousPath = "/unknown/original.mkv";
        await using (var seed = new Models.ApplicationContext(Options))
        {
            var info = Release(infoId, DownloadUrl, publishedAt);
            info.IsDownloadFinished = true;
            info.FileStore = "local";
            info.StorePath = "/missing-media";
            seed.AddRange(info, Mapping(infoId, PreviousPath));
            await seed.SaveChangesAsync();
        }

        var bundle = new LogicalDataBundle(
            1,
            DateTimeOffset.UtcNow,
            "1.0.0",
            LogicalDataCategory.Feeds | LogicalDataCategory.MetadataCorrections,
            [new LogicalFeed(Guid.NewGuid(), "https://example.com/new.xml", "New", DateTimeOffset.UtcNow)],
            [],
            [],
            [
                new LogicalMetadataCorrection(
                    Guid.NewGuid(),
                    DownloadUrl,
                    "[Group] Example - 02",
                    publishedAt,
                    "tv:correct",
                    "Correct Show",
                    "Correct Show",
                    null,
                    "corrected description",
                    2,
                    5,
                    "Correct Group",
                    DateTimeOffset.UtcNow)
            ],
            [],
            null);
        var mapper = new Mock<IFileMapper>(MockBehavior.Strict);
        mapper.Setup(candidate => candidate.PreviewDownloadAsync(
                It.IsAny<AnimationInfo>(),
                CancellationToken.None))
            .ReturnsAsync((FileMappingPreview?)null);

        await using (var importing = new Models.ApplicationContext(Options))
        {
            await Assert.ThrowsAsync<LogicalDataImportConflictException>(() =>
                Repository(importing, mapper.Object).ImportAsync(
                    bundle,
                    LogicalImportConflictStrategy.Overwrite,
                    Guid.Empty,
                    CancellationToken.None));
        }

        await using var verification = new Models.ApplicationContext(Options);
        Assert.AreEqual(0, await verification.Feeds.CountAsync());
        Assert.AreEqual(0, await verification.MetadataReviewOperations.CountAsync());
        var infoAfter = await verification.AnimationInfo.SingleAsync();
        Assert.AreEqual("uncorrected", infoAfter.Description);
        Assert.AreEqual(0, infoAfter.StateVersion);
        Assert.AreEqual(PreviousPath, (await verification.FileMappings.SingleAsync()).VirtualPath);
    }

    [TestMethod]
    public async Task FailConflictStrategyRollsBackEarlierItems()
    {
        await using (var seed = new Models.ApplicationContext(Options))
        {
            seed.Feeds.Add(new Models.Feed
            {
                Id = Guid.NewGuid(),
                Url = "https://example.com/existing.xml",
                Name = "Existing",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        var bundle = new LogicalDataBundle(
            1,
            DateTimeOffset.UtcNow,
            "1.0.0",
            LogicalDataCategory.Feeds,
            [
                new LogicalFeed(Guid.NewGuid(), "https://example.com/new.xml", "New", DateTimeOffset.UtcNow),
                new LogicalFeed(Guid.NewGuid(), "https://example.com/existing.xml", "Changed", DateTimeOffset.UtcNow)
            ],
            [],
            [],
            [],
            [],
            null);

        await using (var importing = new Models.ApplicationContext(Options))
        {
            await Assert.ThrowsAsync<LogicalDataImportConflictException>(() =>
                Repository(importing).ImportAsync(
                    bundle,
                    LogicalImportConflictStrategy.Fail,
                    Guid.Empty,
                    CancellationToken.None));
        }

        await using var verification = new Models.ApplicationContext(Options);
        Assert.AreEqual(1, await verification.Feeds.CountAsync());
        Assert.AreEqual("Existing", (await verification.Feeds.SingleAsync()).Name);
    }

    private static Models.Animation Animation(string tmdbId, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            TmdbId = tmdbId,
            Name = name,
            OriginalName = name,
            PosterPath = "/poster.jpg"
        };

    private static Models.AnimationInfo Release(
        Guid id,
        string downloadUrl,
        DateTimeOffset publishedAt) =>
        new()
        {
            Id = id,
            Title = "[Group] Example - 02",
            Description = "uncorrected",
            PublishTime = publishedAt,
            DownloadUrl = downloadUrl,
            DownloadType = "torrent",
            IsAiProcessed = true,
            MetadataStatus = MetadataReviewStatus.LowConfidence
        };

    private static Models.FileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new()
        {
            Id = Guid.NewGuid(),
            AnimationInfoId = animationInfoId,
            VirtualPath = virtualPath,
            PhysicalPath = "/media/example.mkv",
            FileStore = "local"
        };

    private static LogicalDataTransferRepository Repository(
        Models.ApplicationContext context,
        IFileMapper? fileMapper = null) =>
        new(context, fileMapper ?? Mock.Of<IFileMapper>());
}
