using Microsoft.EntityFrameworkCore;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Keeps playback state attached to the physical file when metadata review or
/// inference changes its virtual path.
/// </summary>
internal static class PlaybackProgressMappingMigrator
{
    public static async Task MigrateAsync(
        Models.ApplicationContext context,
        Guid animationInfoId,
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings,
        CancellationToken cancellationToken)
    {
        if (previousMappings.Count == 0) return;

        var pathTargets = BuildPathTargets(previousMappings, replacementMappings);
        var affectedPaths = pathTargets.Keys
            .Concat(pathTargets.Values.OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (affectedPaths.Length == 0) return;

        var existing = await context.PlaybackProgresses
            .AsNoTracking()
            .Where(progress => progress.AnimationInfoId == animationInfoId
                               && affectedPaths.Contains(progress.VirtualPath))
            .ToListAsync(cancellationToken);
        if (existing.Count == 0) return;

        var migrated = Plan(existing, pathTargets);
        await context.PlaybackProgresses
            .Where(progress => progress.AnimationInfoId == animationInfoId
                               && affectedPaths.Contains(progress.VirtualPath))
            .ExecuteDeleteAsync(cancellationToken);
        if (migrated.Count > 0)
            await context.PlaybackProgresses.AddRangeAsync(migrated, cancellationToken);
    }

    internal static IReadOnlyList<Models.PlaybackProgress> Plan(
        IReadOnlyList<Models.PlaybackProgress> existing,
        IReadOnlyDictionary<string, string?> pathTargets)
    {
        return existing
            .Select(progress => new
            {
                Progress = progress,
                Target = pathTargets.TryGetValue(progress.VirtualPath, out var target)
                    ? target
                    : progress.VirtualPath
            })
            .Where(item => item.Target is not null)
            .GroupBy(
                item => (item.Progress.UserId, VirtualPath: item.Target!),
                item => item.Progress)
            .Select(group =>
            {
                // A previous interrupted remap may already have a row at the target.
                // Preserve whichever row represents the latest user action.
                var latest = group
                    .OrderByDescending(progress => progress.UpdatedAt)
                    .ThenByDescending(progress => progress.IsWatched)
                    .First();
                return new Models.PlaybackProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = latest.UserId,
                    AnimationInfoId = latest.AnimationInfoId,
                    VirtualPath = group.Key.VirtualPath,
                    PositionSeconds = latest.PositionSeconds,
                    DurationSeconds = latest.DurationSeconds,
                    IsWatched = latest.IsWatched,
                    UpdatedAt = latest.UpdatedAt,
                    WatchedAt = latest.WatchedAt
                };
            })
            .ToList();
    }

    private static IReadOnlyDictionary<string, string?> BuildPathTargets(
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings)
    {
        var replacementsByFile = replacementMappings
            .GroupBy(mapping => new FileIdentity(mapping.FileStore, mapping.PhysicalPath))
            .ToDictionary(group => group.Key, group => group.ToList());

        return previousMappings.ToDictionary(
            mapping => mapping.VirtualPath,
            mapping =>
            {
                if (!replacementsByFile.TryGetValue(
                        new FileIdentity(mapping.FileStore, mapping.PhysicalPath),
                        out var candidates))
                    return null;

                var exact = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.VirtualPath, mapping.VirtualPath, StringComparison.Ordinal));
                return exact?.VirtualPath ?? (candidates.Count == 1 ? candidates[0].VirtualPath : null);
            },
            StringComparer.Ordinal);
    }

    private sealed record FileIdentity(string FileStore, string PhysicalPath);
}
