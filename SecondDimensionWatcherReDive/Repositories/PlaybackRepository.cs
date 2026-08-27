using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed partial class PlaybackRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions) : IPlaybackRepository
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".webm", ".avi", ".flv", ".wmv", ".mov", ".m4v", ".ts", ".m2ts"
    };

    public async Task<PlaybackProgress?> FindProgressAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        CancellationToken cancellationToken)
    {
        var entity = await context.PlaybackProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(
                progress => progress.UserId == userId
                            && progress.AnimationInfoId == animationInfoId
                            && progress.VirtualPath == virtualPath,
                cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<IReadOnlyList<PlaybackProgress>> GetStatesAsync(
        Guid userId,
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        var entities = await context.PlaybackProgresses
            .AsNoTracking()
            .Where(progress => progress.UserId == userId
                               && progress.AnimationInfoId == animationInfoId)
            .OrderBy(progress => progress.VirtualPath)
            .ToListAsync(cancellationToken);
        return entities.Select(progress => progress.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<ContinueWatching>> GetContinueWatchingAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        // Fetch a small amount of headroom because obsolete/non-video mappings are filtered below.
        var fetchLimit = Math.Min(limit * 3, 300);
        var entities = await context.PlaybackProgresses
            .AsNoTracking()
            .Include(progress => progress.AnimationInfo!)
            .ThenInclude(info => info.Animation)
            .Include(progress => progress.AnimationInfo!)
            .ThenInclude(info => info.Group)
            .Where(progress => progress.UserId == userId
                               && !progress.IsWatched
                               && progress.PositionSeconds > 0
                               && context.FileMappings.Any(mapping =>
                                   mapping.AnimationInfoId == progress.AnimationInfoId
                                   && mapping.VirtualPath == progress.VirtualPath))
            .OrderByDescending(progress => progress.UpdatedAt)
            .Take(fetchLimit)
            .ToListAsync(cancellationToken);

        return entities
            .Where(progress => progress.AnimationInfo is not null
                               && IsVideo(progress.VirtualPath)
                               && IsAddressable(progress.AnimationInfo, progress.VirtualPath))
            .Select(progress => new ContinueWatching(
                progress.ToRecord(),
                CreateMedia(progress.AnimationInfo!, progress.VirtualPath)))
            .Take(limit)
            .ToList();
    }

    public async Task<PlaybackProgress> UpsertProgressAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        double positionSeconds,
        double durationSeconds,
        bool markWatched,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        return await ExecuteMappingWriteAsync(
            userId,
            animationInfoId,
            virtualPath,
            async writeContext =>
            {
                var id = Guid.NewGuid();
                DateTimeOffset? watchedAt = markWatched ? updatedAt : null;
                await writeContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO "PlaybackProgresses" AS current
                         ("Id", "UserId", "AnimationInfoId", "VirtualPath", "PositionSeconds",
                          "DurationSeconds", "IsWatched", "UpdatedAt", "WatchedAt")
                     VALUES
                         ({id}, {userId}, {animationInfoId}, {virtualPath}, {positionSeconds},
                          {durationSeconds}, {markWatched}, {updatedAt}, {watchedAt})
                     ON CONFLICT ("UserId", "AnimationInfoId", "VirtualPath") DO UPDATE SET
                         "PositionSeconds" = EXCLUDED."PositionSeconds",
                         "DurationSeconds" = EXCLUDED."DurationSeconds",
                         "IsWatched" = current."IsWatched" OR EXCLUDED."IsWatched",
                         "UpdatedAt" = EXCLUDED."UpdatedAt",
                         "WatchedAt" = CASE
                             WHEN current."IsWatched" THEN current."WatchedAt"
                             WHEN EXCLUDED."IsWatched" THEN EXCLUDED."WatchedAt"
                             ELSE NULL
                         END
                     WHERE current."UpdatedAt" <= EXCLUDED."UpdatedAt"
                     """,
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task<PlaybackProgress> SetWatchedAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        bool isWatched,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        return await ExecuteMappingWriteAsync(
            userId,
            animationInfoId,
            virtualPath,
            async writeContext =>
            {
                var id = Guid.NewGuid();
                DateTimeOffset? watchedAt = isWatched ? updatedAt : null;
                await writeContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO "PlaybackProgresses" AS current
                         ("Id", "UserId", "AnimationInfoId", "VirtualPath", "PositionSeconds",
                          "DurationSeconds", "IsWatched", "UpdatedAt", "WatchedAt")
                     VALUES
                         ({id}, {userId}, {animationInfoId}, {virtualPath}, {0d},
                          {0d}, {isWatched}, {updatedAt}, {watchedAt})
                     ON CONFLICT ("UserId", "AnimationInfoId", "VirtualPath") DO UPDATE SET
                         "IsWatched" = EXCLUDED."IsWatched",
                         "UpdatedAt" = EXCLUDED."UpdatedAt",
                         "WatchedAt" = EXCLUDED."WatchedAt"
                     WHERE current."UpdatedAt" <= EXCLUDED."UpdatedAt"
                     """,
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task<PlaybackPreferences> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var entity = await context.PlaybackPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(preference => preference.UserId == userId, cancellationToken);
        return entity?.ToRecord()
               ?? new PlaybackPreferences(userId, null, null, null, null, true, DateTimeOffset.UnixEpoch);
    }

    public async Task<PlaybackPreferences> UpsertPreferencesAsync(
        PlaybackPreferences preferences,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "PlaybackPreferences" AS current
                 ("UserId", "SubtitleLanguage", "SubtitleTrackLabel", "AudioLanguage",
                  "AudioTrackLabel", "AutoPlayNext", "UpdatedAt")
             VALUES
                 ({preferences.UserId}, {preferences.SubtitleLanguage}, {preferences.SubtitleTrackLabel},
                  {preferences.AudioLanguage}, {preferences.AudioTrackLabel},
                  {preferences.AutoPlayNext}, {preferences.UpdatedAt})
             ON CONFLICT ("UserId") DO UPDATE SET
                 "SubtitleLanguage" = EXCLUDED."SubtitleLanguage",
                 "SubtitleTrackLabel" = EXCLUDED."SubtitleTrackLabel",
                 "AudioLanguage" = EXCLUDED."AudioLanguage",
                 "AudioTrackLabel" = EXCLUDED."AudioTrackLabel",
                 "AutoPlayNext" = EXCLUDED."AutoPlayNext",
                 "UpdatedAt" = EXCLUDED."UpdatedAt"
             WHERE current."UpdatedAt" <= EXCLUDED."UpdatedAt"
             """,
            cancellationToken);

        return await GetPreferencesAsync(preferences.UserId, cancellationToken);
    }

    public async Task<PlaybackMedia?> GetNextMediaAsync(
        Guid animationInfoId,
        string virtualPath,
        CancellationToken cancellationToken)
    {
        var selectedInfo = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .FirstOrDefaultAsync(info => info.Id == animationInfoId, cancellationToken);
        if (selectedInfo?.Animation is null) return null;

        var infos = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => info.IsDownloadFinished
                           && info.Animation != null
                           && info.Animation.Id == selectedInfo.Animation.Id)
            .ToListAsync(cancellationToken);
        if (infos.Count == 0) return null;

        var infoById = infos.ToDictionary(info => info.Id);
        var infoIds = infoById.Keys.ToArray();
        var mappings = await context.FileMappings
            .AsNoTracking()
            .Where(mapping => infoIds.Contains(mapping.AnimationInfoId))
            .OrderBy(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);

        var media = mappings
            .Where(mapping => IsVideo(mapping.VirtualPath)
                              && IsAddressable(infoById[mapping.AnimationInfoId], mapping.VirtualPath))
            .Select(mapping => CreateMedia(infoById[mapping.AnimationInfoId], mapping.VirtualPath))
            .ToList();
        return PlaybackSequence.FindNext(media, animationInfoId, virtualPath);
    }

    private async Task<PlaybackProgress> ExecuteMappingWriteAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        Func<Models.ApplicationContext, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            // Mapping replacement takes the same aggregate row lock (after its
            // namespace lock). Playback writes only need the aggregate lock, so
            // unrelated shows/users are not globally serialized every ten seconds.
            var animationInfo = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                animationInfoId,
                cancellationToken);
            if (animationInfo is null) return null;

            var mappingExists = await writeContext.FileMappings
                .AsNoTracking()
                .AnyAsync(mapping => mapping.AnimationInfoId == animationInfoId
                                     && mapping.VirtualPath == virtualPath,
                    cancellationToken);
            if (!mappingExists) return null;

            await writeAsync(writeContext);
            var persisted = await writeContext.PlaybackProgresses
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    progress => progress.UserId == userId
                                && progress.AnimationInfoId == animationInfoId
                                && progress.VirtualPath == virtualPath,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return persisted?.ToRecord();
        });

        return result ?? throw new PlaybackMappingChangedException();
    }

    private static PlaybackMedia CreateMedia(Models.AnimationInfo info, string virtualPath)
    {
        var (parsedSeason, parsedEpisode) = ParseSeasonEpisode(virtualPath);
        return new PlaybackMedia(
            info.Id,
            virtualPath,
            GetRelativePath(info, virtualPath),
            info.Title,
            info.Animation?.Id,
            info.Animation?.Name,
            info.Animation?.PosterPath,
            info.Group?.Id,
            info.Group?.Name,
            info.Season ?? parsedSeason,
            info.Episode ?? parsedEpisode,
            info.PublishTime);
    }

    private static string GetRelativePath(Models.AnimationInfo info, string virtualPath)
    {
        var root = GetVirtualRoot(info);
        return virtualPath.StartsWith(root + "/", StringComparison.Ordinal)
            ? virtualPath[(root.Length + 1)..]
            : virtualPath.TrimStart('/');
    }

    private static bool IsAddressable(Models.AnimationInfo info, string virtualPath)
    {
        var root = GetVirtualRoot(info);
        return virtualPath.StartsWith(root + "/", StringComparison.Ordinal);
    }

    private static string GetVirtualRoot(Models.AnimationInfo info) =>
        info.Animation is null || info.Season is null
            ? "/unknown"
            : $"/{SanitizePathSegment(info.Animation.Name)}/{SanitizePathSegment(info.Group?.Name ?? "Unknown")}";

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(character =>
            invalid.Contains(character) || character == '/' ? '_' : character)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }

    private static bool IsVideo(string virtualPath) =>
        VideoExtensions.Contains(Path.GetExtension(virtualPath));

    private static (int? Season, int? Episode) ParseSeasonEpisode(string virtualPath)
    {
        var match = SeasonEpisodeRegex().Match(Path.GetFileNameWithoutExtension(virtualPath));
        return match.Success
            ? (int.Parse(match.Groups["season"].Value), int.Parse(match.Groups["episode"].Value))
            : (null, null);
    }

    [GeneratedRegex(@"(?i)(?:^|[ ._\-])S(?<season>\d{1,3})E(?<episode>\d{1,4})(?:$|[^0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeRegex();
}
