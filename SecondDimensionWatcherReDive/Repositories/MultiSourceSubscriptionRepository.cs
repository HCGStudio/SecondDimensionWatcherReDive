using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.LibraryCompletion;
namespace SecondDimensionWatcherReDive.Repositories;

public sealed class MultiSourceSubscriptionRepository(Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> options,
    ISubscriptionAutomationMatcher matcher) : IMultiSourceSubscriptionRepository
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
            var saved = await SaveInTransactionAsync(write, input, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return saved;
        });

    // The caller owns the transaction and mapping lock, allowing logical imports
    // to apply feeds and source ownership as one atomic operation.
    internal static async Task<MultiSourceSubscription> SaveInTransactionAsync(Models.ApplicationContext write,
        MultiSourceSubscription input, CancellationToken cancellationToken)
    {
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
        return ToRecord(entity);
    }

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
        var pending = write.AnimationInfo.Where(info => info.SourceFeedId != null && feedIds.Contains(info.SourceFeedId.Value)
            && !info.IsDownloadTracked && !info.IsDownloadFinished && !info.IsRetiredRelease
            && info.DownloadAttemptId == null
            && ((info.AutomationDisposition == null && info.DownloadCancellationId == null)
                || info.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed));
        // Fully compensated failures belong to the old shared policy. Hide their
        // obsolete Todo immediately, then let restoration apply the current feed policy.
        // A finalized failure's retained cancellation id remains an idempotency tombstone.
        var todoKeys = pending.Select(info => "automation:" + info.Id.ToString());
        await write.TodoItemStates.Where(state => todoKeys.Contains(state.Key)).ExecuteDeleteAsync(cancellationToken);
        await pending.ExecuteUpdateAsync(setters => setters
                .SetProperty(info => info.AutomationDisposition, (SubscriptionAutomationDisposition?)null)
                .SetProperty(info => info.AutomationExplanationJson, (string?)null)
                .SetProperty(info => info.StandaloneAutomationPending, true)
                .SetProperty(info => info.StateVersion, info => info.StateVersion + 1), cancellationToken);
    }

    internal static bool MatchesAutomaticSnapshot(Models.MultiSourceSubscription current,
        MultiSourceSubscription expected) =>
        current.Mode == "AutoDownload" && MatchesSnapshot(current, expected);

    internal static bool MatchesSnapshot(Models.MultiSourceSubscription current,
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

    public async Task<IReadOnlyList<MultiSourceFeedStatus>> GetSourceStatusAsync(Guid id, CancellationToken cancellationToken) =>
        (await GetSourceStatusesAsync([id], cancellationToken)).GetValueOrDefault(id) ?? [];

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MultiSourceFeedStatus>>> GetSourceStatusesAsync(
        IReadOnlyCollection<Guid> subscriptionIds, CancellationToken cancellationToken)
    {
        var ids = subscriptionIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, IReadOnlyList<MultiSourceFeedStatus>>();
        var rows = await context.Database.SqlQuery<SourceStatusRow>($"""
            WITH selected_sources AS (
                SELECT "SubscriptionId", "FeedId", "Priority"
                FROM "MultiSourceFeeds" WHERE "SubscriptionId" = ANY({ids})
            ), latest AS (
                SELECT DISTINCT ON (release."SourceFeedId")
                    release."SourceFeedId", release."PublishTime", release."Title",
                    (COUNT(*) FILTER (WHERE release."Season" IS NULL OR release."Season" <= 0
                        OR release."Episode" IS NULL OR release."Episode" <= 0
                        OR release."MetadataStatus" NOT IN ({(int)MetadataReviewStatus.Identified}, {(int)MetadataReviewStatus.Reviewed})
                        OR release."Title" COLLATE "C" ~ {LibraryCompletionService.PostgreSqlBatchTitlePattern})
                        OVER (PARTITION BY release."SourceFeedId"))::integer AS "UnidentifiedCount"
                FROM "AnimationInfo" AS release
                WHERE release."SourceFeedId" IN (SELECT "FeedId" FROM selected_sources)
                ORDER BY release."SourceFeedId", release."PublishTime" DESC, release."Id" DESC
            )
            SELECT source."SubscriptionId", source."FeedId", COALESCE(feed."Name", feed."Url", '') AS "Name",
                source."Priority", latest."PublishTime" AS "LatestPublishedAt", latest."Title" AS "LatestTitle",
                COALESCE(latest."UnidentifiedCount", 0) AS "UnidentifiedCount"
            FROM selected_sources AS source
            LEFT JOIN "Feeds" AS feed ON feed."Id" = source."FeedId"
            LEFT JOIN latest ON latest."SourceFeedId" = source."FeedId"
            ORDER BY source."SubscriptionId", source."Priority", source."FeedId"
            """).ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.SubscriptionId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<MultiSourceFeedStatus>)group.Select(row => new MultiSourceFeedStatus(
                row.FeedId, row.Name, row.Priority, row.LatestPublishedAt, row.LatestTitle, row.UnidentifiedCount)).ToList());
    }

    public async Task<IReadOnlyList<MultiSourceEpisodeDecision>> GetDecisionsAsync(Guid id, CancellationToken cancellationToken) =>
        (await GetDecisionsAsync([id], cancellationToken)).GetValueOrDefault(id) ?? [];

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<MultiSourceEpisodeDecision>>> GetDecisionsAsync(
        IReadOnlyCollection<Guid> subscriptionIds, CancellationToken cancellationToken)
    {
        var ids = subscriptionIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, IReadOnlyList<MultiSourceEpisodeDecision>>();
        var decisions = await (from decision in context.Set<Models.MultiSourceEpisodeDecision>().AsNoTracking()
                               join release in context.AnimationInfo.AsNoTracking()
                                   on decision.SelectedReleaseId equals (Guid?)release.Id into releases
                               from release in releases.DefaultIfEmpty()
                               where ids.Contains(decision.SubscriptionId)
                               orderby decision.SubscriptionId, decision.Episode
                               select new MultiSourceEpisodeDecision(decision.SubscriptionId, decision.Episode,
                                   decision.WaitStartedAt, decision.WaitUntil, decision.SelectedReleaseId,
                                   decision.Outcome, decision.Reason, decision.UpdatedAt,
                                   release == null ? null : release.Title,
                                   release == null ? null : release.SourceFeedId)).ToListAsync(cancellationToken);
        return decisions.GroupBy(decision => decision.SubscriptionId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<MultiSourceEpisodeDecision>)group.ToList());
    }

    private sealed record SourceStatusRow(Guid SubscriptionId, Guid FeedId, string Name, int Priority,
        DateTimeOffset? LatestPublishedAt, string? LatestTitle, int UnidentifiedCount);

    public Task<bool> PruneUnavailableDecisionsAsync(MultiSourceSubscription expectedSubscription,
        CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = new Models.ApplicationContext(options);
            await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(write, cancellationToken);
            var subscription = await write.Set<Models.MultiSourceSubscription>().AsNoTracking()
                .Include(value => value.Sources)
                .SingleOrDefaultAsync(value => value.Id == expectedSubscription.Id, cancellationToken);
            if (subscription is null || !MatchesSnapshot(subscription, expectedSubscription)) return false;

            // Keep terminal failures and records owned by active download/upgrade
            // workflows. Only unfinished choices can become orphaned confirmations.
            var pending = write.Set<Models.MultiSourceEpisodeDecision>().Where(decision =>
                decision.SubscriptionId == subscription.Id &&
                (decision.Outcome == "waiting" || decision.Outcome == "ready" ||
                 decision.Outcome == "unavailable" || decision.Outcome == "notified" ||
                 decision.Outcome == "pending_confirmation"));
            var episodes = await pending.Select(decision => decision.Episode).ToArrayAsync(cancellationToken);
            if (episodes.Length > 0)
            {
                var feedIds = subscription.Sources.Select(source => source.FeedId).ToArray();
                var reliableEpisodes = new HashSet<int>();
                // Re-read membership and reliability under the same lock as metadata
                // changes and SaveDecision; never delete from the coordinator's old list.
                await foreach (var release in write.AnimationInfo.AsNoTracking().Where(info =>
                                   info.Animation != null && info.Animation.TmdbId == subscription.TmdbId &&
                                   info.Season == subscription.Season && info.Episode != null &&
                                   episodes.Contains(info.Episode.Value) && info.MediaLibraryMissingSince == null &&
                                   !info.IsRetiredRelease && info.SourceFeedId != null &&
                                   feedIds.Contains(info.SourceFeedId.Value))
                               .Select(info => new { info.Season, info.Episode, info.MetadataStatus, info.Title })
                               .AsAsyncEnumerable().WithCancellation(cancellationToken))
                    if (LibraryCompletionService.IsReliable(release.Season, release.Episode, release.MetadataStatus, release.Title))
                        reliableEpisodes.Add(release.Episode!.Value);
                var unavailable = episodes.Where(episode => !reliableEpisodes.Contains(episode)).ToArray();
                if (unavailable.Length > 0)
                    await pending.Where(decision => unavailable.Contains(decision.Episode))
                        .ExecuteDeleteAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        });

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
                    || !active && (!subscription.Sources.Any(source => source.FeedId == selected.SourceFeedId)
                        || selected.MediaLibraryMissingSince is not null || selected.IsRetiredRelease
                        || !LibraryCompletionService.IsReliable(selected.Season, selected.Episode,
                            selected.MetadataStatus, selected.Title)))
                    return null;
                // Failed submissions can still be completing their compensation;
                // persist that outcome rather than treating it as a new offer.
                if (!active && decision.Outcome != "failed")
                {
                    var policy = ToRecord(subscription).ToPolicy(selected.SourceFeedId!.Value);
                    var evaluation = matcher.Evaluate(policy, new AnimationAddRequest(
                        selected.PublishTime, selected.Title, selected.Description, selected.DownloadUrl,
                        selected.DownloadType, selected.AdditionalDownloadInfo, selected.SourceFeedId, selected.ReleaseSizeBytes));
                    if (LibraryCompletionService.GetIneligibilityReason(selected.ToRecord(), evaluation) is not null)
                        return null;
                }
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
}
