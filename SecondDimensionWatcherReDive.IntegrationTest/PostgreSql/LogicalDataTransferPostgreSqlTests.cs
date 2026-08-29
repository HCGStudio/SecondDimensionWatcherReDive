using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
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
            bundle = await new LogicalDataTransferRepository(source).ExportAsync(
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
        var repository = new LogicalDataTransferRepository(target);
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

            bundle = await new LogicalDataTransferRepository(source).ExportAsync(
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

        var result = await new LogicalDataTransferRepository(target).ImportAsync(
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
                new LogicalDataTransferRepository(importing).ImportAsync(
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
}
