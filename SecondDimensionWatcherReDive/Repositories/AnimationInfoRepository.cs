using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Repositories;

public class AnimationInfoRepository(Models.ApplicationContext context) : IAnimationInfoRepository
{
    public async Task<PagedResult<AnimationInfo>> GetPagedAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var coreQuery = context.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Group)
            .Include(i => i.Animation)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync(cancellationToken);
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new PagedResult<AnimationInfo>(data.Select(e => e.ToRecord()).ToList(), totalCount);
    }

    public async Task<AnimationGroupedResult> GetGroupedAsync(CancellationToken cancellationToken)
    {
        var allItems = await context.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Animation)
            .Include(i => i.Group)
            .OrderByDescending(i => i.PublishTime)
            .ToListAsync(cancellationToken);

        var categorized = allItems
            .Where(i => i.Animation != null)
            .GroupBy(i => i.Animation!.Id)
            .Select(g =>
            {
                var animation = g.First().Animation!;
                var episodes = g
                    .OrderBy(i => i.Season)
                    .ThenBy(i => i.Episode)
                    .Select(i => i.ToRecord())
                    .ToList();
                return new AnimationWithEpisodesResult(
                    animation.TmdbId,
                    animation.Name,
                    animation.OriginalName,
                    animation.PosterPath,
                    episodes.Count,
                    episodes);
            })
            .OrderByDescending(a => a.Episodes.Max(e => e.PublishTime))
            .ToList();

        var uncategorized = allItems
            .Where(i => i.Animation == null)
            .Select(i => i.ToRecord())
            .ToList();

        return new AnimationGroupedResult(categorized, uncategorized);
    }

    public async Task<PagedResult<AnimationInfo>> GetDownloadingPagedAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var coreQuery = context.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Group)
            .Include(i => i.Animation)
            .Where(i => i.IsDownloadTracked && !i.IsDownloadFinished)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync(cancellationToken);
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new PagedResult<AnimationInfo>(data.Select(e => e.ToRecord()).ToList(), totalCount);
    }

    public async Task<PagedResult<AnimationInfo>> GetDownloadedPagedAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var coreQuery = context.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Group)
            .Include(i => i.Animation)
            .Where(i => i.IsDownloadFinished)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync(cancellationToken);
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new PagedResult<AnimationInfo>(data.Select(e => e.ToRecord()).ToList(), totalCount);
    }

    public async Task<AnimationInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo.FindAsync([id], cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<AnimationInfo?> FindByIdWithAnimationAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo
            .Include(a => a.Animation)
            .Include(a => a.Group)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return entity?.ToRecord();
    }

    public async IAsyncEnumerable<AnimationInfo> GetUnfinishedTorrentDownloadsAsync()
    {
        await foreach (var info in context.AnimationInfo
                           .Where(i => i.IsDownloadTracked
                                       && !i.IsDownloadFinished
                                       && i.DownloadType == FileDownloadTypes.TorrentDownload)
                           .AsAsyncEnumerable())
        {
            yield return info.ToRecord();
        }
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetPendingInferenceAsync(int maxRetryCount, CancellationToken cancellationToken)
    {
        var entities = await context.AnimationInfo
            .Where(i => !i.IsAiProcessed && i.AiRetryCount < maxRetryCount)
            .OrderBy(i => i.PublishTime)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<AnimationInfo?> FindByTitleAsync(string title, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo
            .FirstOrDefaultAsync(i => i.Title == title, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task AddAsync(AnimationInfo info, CancellationToken cancellationToken)
    {
        await context.AnimationInfo.AddAsync(info.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AnimationInfo info, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo.FindAsync([info.Id], cancellationToken)
                     ?? throw new InvalidOperationException($"AnimationInfo {info.Id} not found");
        info.ApplyTo(entity);

        if (info.Animation != null)
            entity.Animation = await context.Animations.FindAsync([info.Animation.Id], cancellationToken);

        if (info.Group != null)
            entity.Group = await context.AnimationGroups.FindAsync([info.Group.Id], cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }
}
