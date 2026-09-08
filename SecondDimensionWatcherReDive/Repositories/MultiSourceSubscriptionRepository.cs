using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.LibraryCompletion;
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
            var removedFeedIds = entity?.Sources.Where(source => !feedIds.Contains(source.FeedId))
                .Select(source => source.FeedId).ToArray() ?? [];
            var savedAt = DateTimeOffset.UtcNow;
            if (entity == null)
            {
                entity = new Models.MultiSourceSubscription { Id = input.Id, CreatedAt = savedAt };
                write.Add(entity);
            }
            else if (entity.TmdbId != input.TmdbId || entity.Season != input.Season)
            {
                await write.Set<Models.MultiSourceEpisodeDecision>().Where(x => x.SubscriptionId == input.Id).ExecuteDeleteAsync(cancellationToken);
                // CreatedAt is the wait epoch for this target. Previously
                // collected fallback releases must wait again after retargeting.
                entity.CreatedAt = savedAt;
            }
            else if (!entity.Sources.OrderBy(source => source.Priority).Select(source => source.FeedId).SequenceEqual(feedIds))
                await ClearPendingDecisionsAsync(write, input.Id, cancellationToken);
            entity.Name = input.Name; entity.TmdbId = input.TmdbId; entity.Season = input.Season;
            entity.WaitMinutes = input.WaitMinutes; entity.Mode = input.Mode;
            entity.SubtitleGroups = input.SubtitleGroups.ToArray(); entity.Resolutions = input.Resolutions.ToArray();
            entity.Codecs = input.Codecs.ToArray(); entity.Languages = input.Languages.ToArray();
            entity.MinSizeBytes = input.MinSizeBytes; entity.MaxSizeBytes = input.MaxSizeBytes;
            entity.ExcludedKeywords = input.ExcludedKeywords.ToArray(); entity.EnableVersionUpgrade = input.EnableVersionUpgrade;
            entity.MinimumUpgradeScore = input.MinimumUpgradeScore; entity.UpgradeRollbackHours = input.UpgradeRollbackHours;
            entity.UpdatedAt = savedAt;
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
            await ScheduleStandaloneRestorationAsync(write, removedFeedIds, cancellationToken);
            // Source ownership and pending standalone actions change atomically.
            // Keep already tracked attempts and downloaded media under their saga.
            var pending = write.AnimationInfo.Where(info => info.SourceFeedId != null
                && feedIds.Contains(info.SourceFeedId.Value) && !info.IsDownloadTracked && !info.IsDownloadFinished
                && (info.StandaloneAutomationPending || info.AutomationDisposition == SubscriptionAutomationDisposition.Notified
                    || info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation
                    || info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed));
            var todoKeys = pending.Select(info => "automation:" + info.Id.ToString());
            await write.TodoItemStates.Where(state => todoKeys.Contains(state.Key)).ExecuteDeleteAsync(cancellationToken);
            await pending.ExecuteUpdateAsync(setters => setters
                .SetProperty(info => info.AutomationDisposition, (SubscriptionAutomationDisposition?)null)
                .SetProperty(info => info.AutomationExplanationJson, (string?)null)
                .SetProperty(info => info.StandaloneAutomationPending, false)
                .SetProperty(info => info.StateVersion, info => info.StateVersion + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(entity);
        });

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = new Models.ApplicationContext(options);
            await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(write, cancellationToken);
            var feedIds = await write.Set<Models.MultiSourceFeed>().Where(source => source.SubscriptionId == id)
                .Select(source => source.FeedId).ToArrayAsync(cancellationToken);
            var removed = await write.Set<Models.MultiSourceSubscription>()
                .Where(subscription => subscription.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
            if (removed) await ScheduleStandaloneRestorationAsync(write, feedIds, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return removed;
        });

    private static async Task ScheduleStandaloneRestorationAsync(Models.ApplicationContext write,
        Guid[] feedIds, CancellationToken cancellationToken)
    {
        if (feedIds.Length == 0) return;
        await write.AnimationInfo.Where(info => info.SourceFeedId != null && feedIds.Contains(info.SourceFeedId.Value)
            && !info.IsDownloadTracked && !info.IsDownloadFinished && !info.IsRetiredRelease
            && info.DownloadCancellationId == null && info.AutomationDisposition == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(info => info.StandaloneAutomationPending, true)
                .SetProperty(info => info.StateVersion, info => info.StateVersion + 1), cancellationToken);
    }

    internal static bool MatchesAutomaticSnapshot(Models.MultiSourceSubscription current,
        MultiSourceSubscription expected) =>
        current.Mode == "AutoDownload" && MatchesSnapshot(current, expected);

    private static bool MatchesSnapshot(Models.MultiSourceSubscription current,
        MultiSourceSubscription expected) =>
        current.Id == expected.Id && current.Name == expected.Name && current.Mode == expected.Mode
        && current.TmdbId == expected.TmdbId && current.Season == expected.Season
        && current.CreatedAt == expected.CreatedAt && current.UpdatedAt == expected.UpdatedAt
        && current.WaitMinutes == expected.WaitMinutes
        && current.Sources.OrderBy(source => source.Priority).Select(source => source.FeedId).SequenceEqual(expected.FeedIds)
        && current.SubtitleGroups.SequenceEqual(expected.SubtitleGroups)
        && current.Resolutions.SequenceEqual(expected.Resolutions)
        && current.Codecs.SequenceEqual(expected.Codecs)
        && current.Languages.SequenceEqual(expected.Languages)
        && current.MinSizeBytes == expected.MinSizeBytes && current.MaxSizeBytes == expected.MaxSizeBytes
        && current.ExcludedKeywords.SequenceEqual(expected.ExcludedKeywords)
        && current.EnableVersionUpgrade == expected.EnableVersionUpgrade
        && current.MinimumUpgradeScore == expected.MinimumUpgradeScore
        && current.UpgradeRollbackHours == expected.UpgradeRollbackHours;

    public async Task<IReadOnlyList<MultiSourceFeedStatus>> GetSourceStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        var sources = await context.Set<Models.MultiSourceFeed>().AsNoTracking().Where(x => x.SubscriptionId == id).OrderBy(x => x.Priority).ToListAsync(cancellationToken);
        var result = new List<MultiSourceFeedStatus>();
        foreach (var source in sources)
        {
            var feed = await context.Feeds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == source.FeedId, cancellationToken);
            var query = context.AnimationInfo.AsNoTracking().Where(x => x.SourceFeedId == source.FeedId);
            var latest = await query.OrderByDescending(x => x.PublishTime).Select(x => new { x.Title, x.PublishTime }).FirstOrDefaultAsync(cancellationToken);
            var unidentifiedCount = 0;
            await foreach (var release in query.Select(x => new { x.Season, x.Episode, x.MetadataStatus, x.Title })
                .AsAsyncEnumerable().WithCancellation(cancellationToken))
                if (!LibraryCompletionService.IsReliable(release.Season, release.Episode, release.MetadataStatus, release.Title))
                    unidentifiedCount++;
            result.Add(new(source.FeedId, feed?.Name ?? feed?.Url ?? "", source.Priority, latest?.PublishTime, latest?.Title,
                unidentifiedCount));
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

    public async Task<MultiSourceEpisodeDecision?> SaveDecisionAsync(MultiSourceEpisodeDecision decision,
        MultiSourceSubscription expectedSubscription, CancellationToken cancellationToken) =>
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = new Models.ApplicationContext(options);
            await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(write, cancellationToken);
            var subscription = await write.Set<Models.MultiSourceSubscription>().AsNoTracking()
                .Include(value => value.Sources).SingleOrDefaultAsync(value => value.Id == decision.SubscriptionId, cancellationToken);
            if (subscription is null || subscription.Sources.Count == 0
                || !MatchesSnapshot(subscription, expectedSubscription)) return null;
            if (decision.SelectedReleaseId is { } selectedId)
            {
                var selected = await write.AnimationInfo.AsNoTracking().Include(value => value.Animation)
                    .SingleOrDefaultAsync(value => value.Id == selectedId, cancellationToken);
                var active = decision.Outcome is "downloaded" or "downloading" or "mapping_pending" or "upgrading";
                if (selected?.Animation?.TmdbId != subscription.TmdbId || selected.Season != subscription.Season
                    || selected.Episode != decision.Episode
                    || !active && !subscription.Sources.Any(source => source.FeedId == selected.SourceFeedId))
                    return null;
            }
            // All policy/target changes and decision writes share this lock.
            // Only a decision based on the current snapshot can authorize a notification.
            await write.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "MultiSourceEpisodeDecisions" ("SubscriptionId", "Episode", "WaitStartedAt", "WaitUntil", "SelectedReleaseId", "Outcome", "Reason", "UpdatedAt")
                VALUES ({decision.SubscriptionId}, {decision.Episode}, {decision.WaitStartedAt}, {decision.WaitUntil}, {decision.SelectedReleaseId}, {decision.Outcome}, {decision.Reason}, {decision.UpdatedAt})
                ON CONFLICT ("SubscriptionId", "Episode") DO UPDATE SET
                    "WaitStartedAt" = LEAST("MultiSourceEpisodeDecisions"."WaitStartedAt", EXCLUDED."WaitStartedAt"),
                    "WaitUntil" = EXCLUDED."WaitUntil", "SelectedReleaseId" = EXCLUDED."SelectedReleaseId",
                    "Outcome" = EXCLUDED."Outcome", "Reason" = EXCLUDED."Reason", "UpdatedAt" = EXCLUDED."UpdatedAt"
                """, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return decision;
        });

    internal static Task<int> ClearPendingDecisionsAsync(Models.ApplicationContext write, Guid subscriptionId,
        CancellationToken cancellationToken) =>
        write.Set<Models.MultiSourceEpisodeDecision>()
            .Where(decision => decision.SubscriptionId == subscriptionId
                && decision.Outcome != "downloaded" && decision.Outcome != "downloading"
                && decision.Outcome != "mapping_pending" && decision.Outcome != "upgrading")
            .ExecuteDeleteAsync(cancellationToken);

    internal static MultiSourceSubscription ToRecord(Models.MultiSourceSubscription x) => new(x.Id, x.Name, x.TmdbId, x.Season,
        x.Sources.OrderBy(y => y.Priority).Select(y => y.FeedId).ToList(), x.WaitMinutes, x.Mode, x.SubtitleGroups, x.Resolutions,
        x.Codecs, x.Languages, x.MinSizeBytes, x.MaxSizeBytes, x.ExcludedKeywords, x.EnableVersionUpgrade,
        x.MinimumUpgradeScore, x.UpgradeRollbackHours, x.CreatedAt, x.UpdatedAt);
    private static MultiSourceEpisodeDecision ToDecision(Models.MultiSourceEpisodeDecision x) => new(x.SubscriptionId,
        x.Episode, x.WaitStartedAt, x.WaitUntil, x.SelectedReleaseId, x.Outcome, x.Reason, x.UpdatedAt);
}
