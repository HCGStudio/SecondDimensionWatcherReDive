using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PlaybackProgressMappingMigratorTests
{
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
}
