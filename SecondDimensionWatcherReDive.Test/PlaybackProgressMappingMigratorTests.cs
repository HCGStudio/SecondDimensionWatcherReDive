using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PlaybackProgressMappingMigratorTests
{
    [TestMethod]
    public void BuildPathTargets_KnownVirtualPathSurvivesPhysicalRename()
    {
        var infoId = Guid.NewGuid();
        const string virtualPath = "/A Show/Group/A Show S01E01.mkv";
        var previous = CreateMapping(
            infoId,
            virtualPath,
            PhysicalPath("Old", "episode.mkv"));
        var replacement = CreateMapping(
            infoId,
            virtualPath,
            PhysicalPath("New", "episode.mkv"));
        var updatedAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var progress = CreateProgress(Guid.NewGuid(), infoId, virtualPath, 321, updatedAt);
        progress.DurationSeconds = 1440;
        progress.IsWatched = true;
        progress.WatchedAt = updatedAt.AddMinutes(-1);

        var targets = PlaybackProgressMappingMigrator.BuildPathTargets(
            [previous],
            [replacement]);
        var result = PlaybackProgressMappingMigrator.Plan([progress], targets);

        Assert.AreEqual(virtualPath, targets[virtualPath]);
        Assert.HasCount(1, result);
        Assert.AreEqual(virtualPath, result[0].VirtualPath);
        Assert.AreEqual(progress.PositionSeconds, result[0].PositionSeconds);
        Assert.AreEqual(progress.DurationSeconds, result[0].DurationSeconds);
        Assert.AreEqual(progress.UpdatedAt, result[0].UpdatedAt);
        Assert.AreEqual(progress.IsWatched, result[0].IsWatched);
        Assert.AreEqual(progress.WatchedAt, result[0].WatchedAt);
    }

    [TestMethod]
    public void BuildPathTargets_UnknownDirectoryRename_MatchesUniqueRelativeFiles()
    {
        var infoId = Guid.NewGuid();
        var previous = new[]
        {
            CreateMapping(
                infoId,
                "/unknown/Old/Season/E01.mkv",
                PhysicalPath("Old", "Season", "E01.mkv")),
            CreateMapping(
                infoId,
                "/unknown/Old/Season/E01.zh.srt",
                PhysicalPath("Old", "Season", "E01.zh.srt")),
            CreateMapping(
                infoId,
                "/unknown/Old/Season/E02.mkv",
                PhysicalPath("Old", "Season", "E02.mkv"))
        };
        var replacement = new[]
        {
            CreateMapping(
                infoId,
                "/unknown/New/Season/E01.mkv",
                PhysicalPath("New", "Season", "E01.mkv")),
            CreateMapping(
                infoId,
                "/unknown/New/Season/E01.zh.srt",
                PhysicalPath("New", "Season", "E01.zh.srt")),
            CreateMapping(
                infoId,
                "/unknown/New/Season/E02.mkv",
                PhysicalPath("New", "Season", "E02.mkv"))
        };

        var targets = PlaybackProgressMappingMigrator.BuildPathTargets(previous, replacement);
        var orphanTargets = PlaybackProgressMappingMigrator.BuildOrphanPathTargets(
            previous.Select(mapping => mapping.VirtualPath),
            [],
            replacement);
        var firstProgress = CreateProgress(
            Guid.NewGuid(),
            infoId,
            "/unknown/Old/Season/E01.mkv",
            101,
            DateTimeOffset.Parse("2026-08-28T10:00:00Z"));
        var secondProgress = CreateProgress(
            Guid.NewGuid(),
            infoId,
            "/unknown/Old/Season/E02.mkv",
            202,
            DateTimeOffset.Parse("2026-08-28T11:00:00Z"));
        var migrated = PlaybackProgressMappingMigrator.Plan(
            [firstProgress, secondProgress],
            targets);

        Assert.AreEqual(
            "/unknown/New/Season/E01.mkv",
            targets["/unknown/Old/Season/E01.mkv"]);
        Assert.AreEqual(
            "/unknown/New/Season/E01.zh.srt",
            targets["/unknown/Old/Season/E01.zh.srt"]);
        Assert.AreEqual(
            "/unknown/New/Season/E02.mkv",
            targets["/unknown/Old/Season/E02.mkv"]);
        Assert.AreEqual(
            "/unknown/New/Season/E01.mkv",
            orphanTargets["/unknown/Old/Season/E01.mkv"]);
        Assert.AreEqual(
            "/unknown/New/Season/E02.mkv",
            orphanTargets["/unknown/Old/Season/E02.mkv"]);
        Assert.HasCount(2, migrated);
        Assert.IsTrue(migrated.Any(progress =>
            progress.VirtualPath == "/unknown/New/Season/E01.mkv"
            && progress.PositionSeconds == 101));
        Assert.IsTrue(migrated.Any(progress =>
            progress.VirtualPath == "/unknown/New/Season/E02.mkv"
            && progress.PositionSeconds == 202));
    }

    [TestMethod]
    public void BuildPathTargets_TopLevelVideoAndSidecarRename_MatchesUniqueRoles()
    {
        var infoId = Guid.NewGuid();
        var previous = new[]
        {
            CreateMapping(infoId, "/unknown/Old/Old.mkv", PhysicalPath("Old.mkv")),
            CreateMapping(infoId, "/unknown/Old/Old.zh.srt", PhysicalPath("Old.zh.srt"))
        };
        var replacement = new[]
        {
            CreateMapping(infoId, "/unknown/New/New.mkv", PhysicalPath("New.mkv")),
            CreateMapping(infoId, "/unknown/New/New.zh.srt", PhysicalPath("New.zh.srt"))
        };

        var targets = PlaybackProgressMappingMigrator.BuildPathTargets(previous, replacement);
        var orphanTargets = PlaybackProgressMappingMigrator.BuildOrphanPathTargets(
            previous.Select(mapping => mapping.VirtualPath),
            [],
            replacement);

        Assert.AreEqual("/unknown/New/New.mkv", targets["/unknown/Old/Old.mkv"]);
        Assert.AreEqual("/unknown/New/New.zh.srt", targets["/unknown/Old/Old.zh.srt"]);
        Assert.AreEqual("/unknown/New/New.mkv", orphanTargets["/unknown/Old/Old.mkv"]);
    }

    [TestMethod]
    public void BuildPathTargets_AmbiguousFallback_DoesNotChooseArbitrarily()
    {
        var infoId = Guid.NewGuid();
        const string oldVirtualPath = "/unknown/Old/Season/E01.mkv";
        var previous = CreateMapping(
            infoId,
            oldVirtualPath,
            PhysicalPath("Old", "Season", "E01.mkv"));
        var replacements = new[]
        {
            CreateMapping(
                infoId,
                "/unknown/NewA/Season/E01.mkv",
                PhysicalPath("NewA", "Season", "E01.mkv")),
            CreateMapping(
                infoId,
                "/unknown/NewB/Season/E01.mkv",
                PhysicalPath("NewB", "Season", "E01.mkv"))
        };
        var progress = CreateProgress(
            Guid.NewGuid(),
            infoId,
            oldVirtualPath,
            42,
            DateTimeOffset.UtcNow);

        var targets = PlaybackProgressMappingMigrator.BuildPathTargets(
            [previous],
            replacements);
        var result = PlaybackProgressMappingMigrator.Plan([progress], targets);

        Assert.IsNull(targets[oldVirtualPath]);
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void BuildPathTargets_DifferentNestedFileWithUniqueExtension_DoesNotTransferProgress()
    {
        var infoId = Guid.NewGuid();
        const string oldVirtualPath = "/unknown/Release/Season/E02.mkv";
        var previous = CreateMapping(
            infoId,
            oldVirtualPath,
            PhysicalPath("Release", "Season", "E02.mkv"));
        var replacement = CreateMapping(
            infoId,
            "/unknown/Release/Season/E03.mkv",
            PhysicalPath("Release", "Season", "E03.mkv"));

        var targets = PlaybackProgressMappingMigrator.BuildPathTargets(
            [previous],
            [replacement]);

        Assert.IsNull(targets[oldVirtualPath]);
    }

    [TestMethod]
    public void Plan_MetadataRemap_MovesProgressToCanonicalPath()
    {
        var infoId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.Parse("2026-08-28T12:00:00Z");
        var progress = CreateProgress(
            userId,
            infoId,
            "/unknown/release/episode.mkv",
            321,
            updatedAt);
        var targets = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [progress.VirtualPath] = "/A Show/Sub Group/A Show S01E01.mkv"
        };

        var result = PlaybackProgressMappingMigrator.Plan([progress], targets);

        Assert.HasCount(1, result);
        Assert.AreEqual("/A Show/Sub Group/A Show S01E01.mkv", result[0].VirtualPath);
        Assert.AreEqual(321, result[0].PositionSeconds);
        Assert.AreEqual(updatedAt, result[0].UpdatedAt);
    }

    [TestMethod]
    public void Plan_TargetAlreadyHasProgress_PreservesLatestUserAction()
    {
        var infoId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string oldPath = "/unknown/episode.mkv";
        const string targetPath = "/A Show/Group/A Show S01E01.mkv";
        var older = CreateProgress(
            userId,
            infoId,
            oldPath,
            100,
            DateTimeOffset.Parse("2026-08-28T10:00:00Z"));
        var newer = CreateProgress(
            userId,
            infoId,
            targetPath,
            500,
            DateTimeOffset.Parse("2026-08-28T11:00:00Z"));
        var targets = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [oldPath] = targetPath
        };

        var result = PlaybackProgressMappingMigrator.Plan([older, newer], targets);

        Assert.HasCount(1, result);
        Assert.AreEqual(targetPath, result[0].VirtualPath);
        Assert.AreEqual(500, result[0].PositionSeconds);
        Assert.AreEqual(newer.UpdatedAt, result[0].UpdatedAt);
    }

    [TestMethod]
    public void Plan_RemovedPhysicalFile_DropsObsoleteProgress()
    {
        var progress = CreateProgress(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "/unknown/deleted.mkv",
            42,
            DateTimeOffset.UtcNow);
        var targets = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [progress.VirtualPath] = null
        };

        var result = PlaybackProgressMappingMigrator.Plan([progress], targets);

        Assert.IsEmpty(result);
    }

    private static SecondDimensionWatcherReDive.Models.PlaybackProgress CreateProgress(
        Guid userId,
        Guid animationInfoId,
        string path,
        double position,
        DateTimeOffset updatedAt) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AnimationInfoId = animationInfoId,
            VirtualPath = path,
            PositionSeconds = position,
            DurationSeconds = 1000,
            UpdatedAt = updatedAt
        };

    private static SecondDimensionWatcherReDive.Models.FileMapping CreateMapping(
        Guid animationInfoId,
        string virtualPath,
        string physicalPath) => new()
        {
            Id = Guid.NewGuid(),
            AnimationInfoId = animationInfoId,
            VirtualPath = virtualPath,
            PhysicalPath = physicalPath,
            FileStore = "local"
        };

    private static string PhysicalPath(params string[] segments) =>
        Path.Combine([Path.GetTempPath(), "sdw-playback-mapping-tests", .. segments]);
}
