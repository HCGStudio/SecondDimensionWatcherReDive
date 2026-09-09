using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

internal static class PlaybackSequence
{
    public static PlaybackMedia? FindNext(
        IReadOnlyList<PlaybackMedia> media,
        Guid animationInfoId,
        string virtualPath)
    {
        var current = media.FirstOrDefault(item => item.AnimationInfoId == animationInfoId
                                                   && item.VirtualPath == virtualPath);
        if (current is null) return null;

        if (current.Season is not null && current.Episode is not null)
            return FindNumberedSuccessor(media, current);

        var sameGroup = media.Where(item => item.GroupId == current.GroupId).ToList();
        return FindUnnumberedSuccessor(sameGroup, current)
               ?? FindUnnumberedSuccessor(media, current);
    }

    private static PlaybackMedia? FindNumberedSuccessor(
        IEnumerable<PlaybackMedia> candidates,
        PlaybackMedia current)
    {
        return candidates
                .Where(item => item.Season is not null && item.Episode is not null)
                .Where(item => item.Season > current.Season
                               || item.Season == current.Season && item.Episode > current.Episode)
                .OrderBy(item => item.Season)
                .ThenBy(item => item.Episode)
                // Subgroup preference selects a version of the next episode;
                // it must never skip an earlier episode from another subgroup.
                .ThenByDescending(item => item.GroupId == current.GroupId)
                .ThenBy(item => item.PublishTime)
                .ThenBy(item => item.VirtualPath, StringComparer.Ordinal)
                .FirstOrDefault();
    }

    private static PlaybackMedia? FindUnnumberedSuccessor(
        IEnumerable<PlaybackMedia> candidates,
        PlaybackMedia current)
    {
        // Legacy/unknown entries without parseable episode numbers still get a
        // deterministic publish-order sequence, without guessing across numbered media.
        var ordered = candidates
            .Where(item => item.Season is null || item.Episode is null)
            .OrderBy(item => item.PublishTime)
            .ThenBy(item => item.VirtualPath, StringComparer.Ordinal)
            .ToList();
        var currentIndex = ordered.FindIndex(item =>
            item.AnimationInfoId == current.AnimationInfoId
            && item.VirtualPath == current.VirtualPath);
        return currentIndex >= 0 && currentIndex + 1 < ordered.Count
            ? ordered[currentIndex + 1]
            : null;
    }
}
