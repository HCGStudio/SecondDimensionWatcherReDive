using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileStore;
namespace SecondDimensionWatcherReDive.Repositories;

public sealed class MultiSourceSubscriptionRepository(Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> options) : IMultiSourceSubscriptionRepository
{
    public async Task<IReadOnlyList<MultiSourceSubscription>> GetAllAsync(CancellationToken cancellationToken) =>
        (await context.Set<Models.MultiSourceSubscription>().AsNoTracking().Include(x => x.Sources)
            .OrderBy(x => x.Name).ToListAsync(cancellationToken)).Select(ToRecord).ToList();

    public async Task<MultiSourceSubscription?> FindByFeedIdAsync(Guid feedId, CancellationToken cancellationToken)
    {
        var entity = await context.Set<Models.MultiSourceSubscription>().AsNoTracking().Include(x => x.Sources)
            .FirstOrDefaultAsync(x => x.Sources.Any(y => y.FeedId == feedId), cancellationToken);
        return entity == null ? null : ToRecord(entity);
    }

    public async Task<MultiSourceSubscription> SaveAsync(MultiSourceSubscription input, CancellationToken cancellationToken) =>
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = new Models.ApplicationContext(options);
            await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(write, cancellationToken);
            var feedIds = input.FeedIds.ToArray();
            if (await write.Feeds.CountAsync(x => feedIds.Contains(x.Id), cancellationToken) != feedIds.Length)
                throw new ArgumentException("One or more feeds no longer exist.");
            if (await write.Set<Models.MultiSourceFeed>().AnyAsync(x => feedIds.Contains(x.FeedId) && x.SubscriptionId != input.Id, cancellationToken) ||
                await write.Set<Models.MultiSourceSubscription>().AnyAsync(x => x.TmdbId == input.TmdbId && x.Season == input.Season && x.Id != input.Id, cancellationToken))
                throw new ArgumentException("The season or feed is already linked to a subscription.");
            var entity = await write.Set<Models.MultiSourceSubscription>().Include(x => x.Sources).FirstOrDefaultAsync(x => x.Id == input.Id, cancellationToken);
            if (entity == null)
            {
                entity = new Models.MultiSourceSubscription { Id = input.Id, CreatedAt = DateTimeOffset.UtcNow };
                write.Add(entity);
            }
            else if (entity.TmdbId != input.TmdbId || entity.Season != input.Season)
                await write.Set<Models.MultiSourceEpisodeDecision>().Where(x => x.SubscriptionId == input.Id).ExecuteDeleteAsync(cancellationToken);
            entity.Name = input.Name; entity.TmdbId = input.TmdbId; entity.Season = input.Season;
            entity.WaitMinutes = input.WaitMinutes; entity.Mode = input.Mode;
            entity.SubtitleGroups = input.SubtitleGroups.ToArray(); entity.Resolutions = input.Resolutions.ToArray();
            entity.Codecs = input.Codecs.ToArray(); entity.Languages = input.Languages.ToArray();
            entity.MinSizeBytes = input.MinSizeBytes; entity.MaxSizeBytes = input.MaxSizeBytes;
            entity.ExcludedKeywords = input.ExcludedKeywords.ToArray(); entity.EnableVersionUpgrade = input.EnableVersionUpgrade;
            entity.MinimumUpgradeScore = input.MinimumUpgradeScore; entity.UpgradeRollbackHours = input.UpgradeRollbackHours;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var old in entity.Sources.Where(x => !feedIds.Contains(x.FeedId)).ToList())
            {
                entity.Sources.Remove(old); write.Remove(old);
            }
            for (var priority = 0; priority < feedIds.Length; priority++)
            {
                var source = entity.Sources.FirstOrDefault(x => x.FeedId == feedIds[priority]);
                if (source == null) { source = new Models.MultiSourceFeed { FeedId = feedIds[priority], SubscriptionId = entity.Id }; entity.Sources.Add(source); }
                source.Priority = priority;
            }
            await write.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(entity);
        });

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Set<Models.MultiSourceSubscription>().Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public async Task<IReadOnlyList<MultiSourceFeedStatus>> GetSourceStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        var sources = await context.Set<Models.MultiSourceFeed>().AsNoTracking().Where(x => x.SubscriptionId == id).OrderBy(x => x.Priority).ToListAsync(cancellationToken);
        var result = new List<MultiSourceFeedStatus>();
        foreach (var source in sources)
        {
            var feed = await context.Feeds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == source.FeedId, cancellationToken);
            var query = context.AnimationInfo.AsNoTracking().Where(x => x.SourceFeedId == source.FeedId);
            var latest = await query.OrderByDescending(x => x.PublishTime).Select(x => new { x.Title, x.PublishTime }).FirstOrDefaultAsync(cancellationToken);
            result.Add(new(source.FeedId, feed?.Name ?? feed?.Url ?? "", source.Priority, latest?.PublishTime, latest?.Title,
                await query.CountAsync(x => x.Season == null || x.Episode == null || x.MetadataStatus == MetadataReviewStatus.LowConfidence, cancellationToken)));
        }
        return result;
    }
    public async Task<IReadOnlyList<MultiSourceEpisodeDecision>> GetDecisionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var decisions = await context.Set<Models.MultiSourceEpisodeDecision>().AsNoTracking().Where(x => x.SubscriptionId == id)
            .OrderBy(x => x.Episode).ToListAsync(cancellationToken);
        var releaseIds = decisions.Where(x => x.SelectedReleaseId != null).Select(x => x.SelectedReleaseId!.Value).ToArray();
        var selected = await context.AnimationInfo.AsNoTracking().Where(x => releaseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title, x.SourceFeedId }).ToDictionaryAsync(x => x.Id, cancellationToken);
        return decisions.Select(x =>
        {
            var info = x.SelectedReleaseId is { } releaseId ? selected.GetValueOrDefault(releaseId) : null;
            return ToDecision(x) with { SelectedTitle = info?.Title, SelectedSourceFeedId = info?.SourceFeedId };
        }).ToList();
    }

    public async Task<MultiSourceEpisodeDecision> SaveDecisionAsync(MultiSourceEpisodeDecision decision, CancellationToken cancellationToken)
    {
        // A single atomic upsert retains the first observed release time across replicas/restarts.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MultiSourceEpisodeDecisions" ("SubscriptionId", "Episode", "WaitStartedAt", "WaitUntil", "SelectedReleaseId", "Outcome", "Reason", "UpdatedAt")
            VALUES ({decision.SubscriptionId}, {decision.Episode}, {decision.WaitStartedAt}, {decision.WaitUntil}, {decision.SelectedReleaseId}, {decision.Outcome}, {decision.Reason}, {decision.UpdatedAt})
            ON CONFLICT ("SubscriptionId", "Episode") DO UPDATE SET
                "WaitStartedAt" = LEAST("MultiSourceEpisodeDecisions"."WaitStartedAt", EXCLUDED."WaitStartedAt"),
                "WaitUntil" = EXCLUDED."WaitUntil", "SelectedReleaseId" = EXCLUDED."SelectedReleaseId",
                "Outcome" = EXCLUDED."Outcome", "Reason" = EXCLUDED."Reason", "UpdatedAt" = EXCLUDED."UpdatedAt"
            """, cancellationToken);
        return (await GetDecisionsAsync(decision.SubscriptionId, cancellationToken)).Single(x => x.Episode == decision.Episode);
    }

    internal static MultiSourceSubscription ToRecord(Models.MultiSourceSubscription x) => new(x.Id, x.Name, x.TmdbId, x.Season,
        x.Sources.OrderBy(y => y.Priority).Select(y => y.FeedId).ToList(), x.WaitMinutes, x.Mode, x.SubtitleGroups, x.Resolutions,
        x.Codecs, x.Languages, x.MinSizeBytes, x.MaxSizeBytes, x.ExcludedKeywords, x.EnableVersionUpgrade,
        x.MinimumUpgradeScore, x.UpgradeRollbackHours, x.CreatedAt, x.UpdatedAt);
    private static MultiSourceEpisodeDecision ToDecision(Models.MultiSourceEpisodeDecision x) => new(x.SubscriptionId,
        x.Episode, x.WaitStartedAt, x.WaitUntil, x.SelectedReleaseId, x.Outcome, x.Reason, x.UpdatedAt);
}
