using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Repositories;

internal sealed class WatchlistRepository(Models.ApplicationContext context) : IWatchlistRepository
{
    public async Task<Guid> UpsertAsync(Guid profileId, WatchlistUpdate update, CancellationToken cancellationToken)
    {
        var items = context.Set<Models.WatchlistItem>();
        var item = update.Id.HasValue
            ? await items.SingleOrDefaultAsync(x => x.ProfileId == profileId && x.Id == update.Id, cancellationToken)
            : null;
        if (update.Id.HasValue && item is null) throw new KeyNotFoundException();
        var tmdbId = update.TmdbIdSpecified ? update.TmdbId : item?.TmdbId;
        var mikanId = update.MikanIdSpecified ? update.MikanId : item?.MikanId;
        if (tmdbId is null && mikanId is null)
            throw new ArgumentException("A watchlist entry must retain a TMDB or Mikan link.");
        var matches = await items.Where(x => x.ProfileId == profileId
            && ((tmdbId != null && x.TmdbId == tmdbId) || (mikanId != null && x.MikanId == mikanId)))
            .ToListAsync(cancellationToken);
        item ??= matches.FirstOrDefault();
        if (item is null)
        {
            item = new Models.WatchlistItem { Id = Guid.NewGuid(), ProfileId = profileId };
            items.Add(item);
        }
        // Linking a discovery-only entry to TMDB merges its duplicate in this profile.
        items.RemoveRange(matches.Where(x => x.Id != item.Id));
        if (update.TmdbIdSpecified) item.TmdbId = update.TmdbId;
        if (update.MikanIdSpecified) item.MikanId = update.MikanId;
        item.SubjectKey = item.TmdbId is { } linked ? $"tmdb:{linked}" : $"mikan:{item.MikanId}";
        item.Title = update.Title.Trim();
        item.Status = update.Status;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task<bool> DeleteAsync(Guid profileId, Guid id, CancellationToken cancellationToken) =>
        await context.Set<Models.WatchlistItem>().Where(x => x.ProfileId == profileId && x.Id == id)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<WatchlistEntry>> GetAsync(Guid profileId, DateTimeOffset weekStart, DateTimeOffset weekEnd, CancellationToken cancellationToken)
    {
        var items = await context.Set<Models.WatchlistItem>().AsNoTracking().Where(x => x.ProfileId == profileId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken);
        var tmdbIds = items.Where(x => x.TmdbId != null).Select(x => x.TmdbId!).ToArray();
        var mikanIds = items.Where(x => x.MikanId != null).Select(x => x.MikanId!.Value).ToArray();
        var schedule = await context.SeasonBangumis.AsNoTracking().Where(x => mikanIds.Contains(x.MikanId)).ToListAsync(cancellationToken);
        var releases = await context.AnimationInfo.AsNoTracking().Include(x => x.Animation).Include(x => x.Group)
            .Where(x => x.Animation != null && tmdbIds.Contains(x.Animation.TmdbId) && !x.IsRetiredRelease)
            .OrderByDescending(x => x.PublishTime).ToListAsync(cancellationToken);
        var ids = releases.Select(x => x.Id).ToArray();
        var mappings = await context.FileMappings.AsNoTracking().Where(x => ids.Contains(x.AnimationInfoId)).ToListAsync(cancellationToken);
        var states = await context.PlaybackProgresses.AsNoTracking().Where(x => x.UserId == profileId && ids.Contains(x.AnimationInfoId))
            .ToListAsync(cancellationToken);
        return items.Select(item =>
        {
            var own = releases.Where(x => x.Animation!.TmdbId == item.TmdbId).ToList();
            var candidates = own.SelectMany(release =>
            {
                var videos = mappings.Where(x => x.AnimationInfoId == release.Id && MediaFileTypes.IsVideo(x.VirtualPath))
                    .OrderBy(x => x.VirtualPath).ToArray();
                var root = PlaybackPathResolver.ResolveVirtualPath(release.ToRecord(), null).TrimEnd('/') + "/";
                if (!release.IsDownloadFinished || videos.Length == 0)
                    return new[] { new EpisodeCandidate(release.Id, release.Title, release.Season, release.Episode,
                        release.PublishTime, false, null, 0, false) };
                return videos.Select(video =>
                {
                    var parsed = EpisodePattern.Match(Path.GetFileNameWithoutExtension(video.VirtualPath));
                    var season = parsed.Success ? int.Parse(parsed.Groups["season"].Value) : release.Season;
                    var episode = parsed.Success ? int.Parse(parsed.Groups["episode"].Value) : release.Episode;
                    var state = states.FirstOrDefault(x => x.AnimationInfoId == release.Id && x.VirtualPath == video.VirtualPath);
                    var relative = video.VirtualPath.StartsWith(root, StringComparison.Ordinal) ? video.VirtualPath[root.Length..] : null;
                    return new EpisodeCandidate(release.Id, video.VirtualPath, season, episode, release.PublishTime,
                        relative != null, relative, state?.PositionSeconds ?? 0, state?.IsWatched == true);
                });
            }).ToArray();
            var episodes = candidates.GroupBy(x => x.Episode.HasValue ? $"{x.Season}:{x.Episode}" : $"{x.Id}:{x.Path}")
                .Where(group => !group.Any(x => x.Watched))
                .Select(group => group.OrderByDescending(x => x.Available).ThenByDescending(x => x.Position).ThenByDescending(x => x.Published).First())
                .Where(x => x.Available || (x.Published >= weekStart && x.Published < weekEnd))
                .OrderBy(x => x.Season).ThenBy(x => x.Episode)
                .Select(x => new WatchlistEpisode(x.Id, x.Title, x.Season, x.Episode, x.Published,
                    x.Available ? "downloaded" : "released", x.Path, x.Position)).ToArray();
            var day = schedule.FirstOrDefault(x => x.MikanId == item.MikanId)?.DayOfWeek;
            return new WatchlistEntry(item.Id, item.TmdbId, item.MikanId, item.Title, item.Status,
                day is >= 0 and <= 6 ? day : null, item.UpdatedAt, episodes);
        }).ToArray();
    }

    private sealed record EpisodeCandidate(Guid Id, string Title, int? Season, int? Episode, DateTimeOffset Published,
        bool Available, string? Path, double Position, bool Watched);
    private static readonly System.Text.RegularExpressions.Regex EpisodePattern = new(
        @"(?i)(?:^|[ ._\-])S(?<season>\d{1,3})E(?<episode>\d{1,4})(?:$|[^0-9])",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
}
