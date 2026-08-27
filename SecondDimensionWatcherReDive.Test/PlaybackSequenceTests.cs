using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class PlaybackSequenceTests
{
    [TestMethod]
    public void FindNext_PrefersSameReleaseGroupAndSkipsDuplicateEpisode()
    {
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var current = CreateMedia(1, 2, groupA, DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        var duplicateFromOtherGroup = CreateMedia(1, 2, groupB, DateTimeOffset.Parse("2026-01-03T00:00:00Z"));
        var sameGroupNext = CreateMedia(1, 3, groupA, DateTimeOffset.Parse("2026-01-04T00:00:00Z"));
        var otherGroupNext = CreateMedia(1, 3, groupB, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        var result = PlaybackSequence.FindNext(
            [current, duplicateFromOtherGroup, sameGroupNext, otherGroupNext],
            current.AnimationInfoId,
            current.VirtualPath);

        Assert.AreEqual(sameGroupNext, result);
    }

    [TestMethod]
    public void FindNext_NumberedTerminalEpisode_DoesNotFallBackToDuplicateOrOlderMedia()
    {
        var current = CreateMedia(1, 12, Guid.NewGuid(), DateTimeOffset.Parse("2026-01-12T00:00:00Z"));
        var older = CreateMedia(1, 11, current.GroupId, DateTimeOffset.Parse("2026-01-11T00:00:00Z"));

        var result = PlaybackSequence.FindNext(
            [older, current], current.AnimationInfoId, current.VirtualPath);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void FindNext_UnnumberedLegacyMedia_UsesPublishOrder()
    {
        var group = Guid.NewGuid();
        var first = CreateMedia(null, null, group, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var second = CreateMedia(null, null, group, DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        var result = PlaybackSequence.FindNext(
            [second, first], first.AnimationInfoId, first.VirtualPath);

        Assert.AreEqual(second, result);
    }

    [TestMethod]
    public void FindNext_SameGroupHasNoSuccessor_FallsBackToAnotherRelease()
    {
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        var olderSameGroup = CreateMedia(1, 1, groupA, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var current = CreateMedia(1, 2, groupA, DateTimeOffset.Parse("2026-01-02T00:00:00Z"));
        var nextOtherGroup = CreateMedia(1, 3, groupB, DateTimeOffset.Parse("2026-01-03T00:00:00Z"));

        var result = PlaybackSequence.FindNext(
            [olderSameGroup, current, nextOtherGroup],
            current.AnimationInfoId,
            current.VirtualPath);

        Assert.AreEqual(nextOtherGroup, result);
    }

    private static PlaybackMedia CreateMedia(
        int? season,
        int? episode,
        Guid? groupId,
        DateTimeOffset publishTime)
    {
        var id = Guid.NewGuid();
        var path = $"episode-{id:N}.mkv";
        return new PlaybackMedia(
            id,
            $"/Show/Group/{path}",
            path,
            path,
            Guid.Parse("5e77f2b4-32d8-4a43-a00f-31e6d6ec24f1"),
            "Show",
            null,
            groupId,
            groupId?.ToString(),
            season,
            episode,
            publishTime);
    }
}
