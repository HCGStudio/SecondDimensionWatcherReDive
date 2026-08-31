using System.Runtime.CompilerServices;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Repositories;

public class AnimationInfoRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions) : IAnimationInfoRepository
{
    public async Task<PagedResult<AnimationInfo>> GetPagedAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var coreQuery = context.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Group)
            .Include(i => i.Animation)
            .Where(i => i.MediaLibraryMissingSince == null)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync(cancellationToken);
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new PagedResult<AnimationInfo>(data.Select(e => e.ToRecord()).ToList(), totalCount);
    }

    public async Task<long> GetAnimationCatalogRevisionAsync(CancellationToken cancellationToken)
    {
        return await context.AnimationCatalogStates
            .AsNoTracking()
            .Where(state => state.Id == 1)
            .Select(state => state.Revision)
            .SingleAsync(cancellationToken);
    }

    public async Task<AnimationCatalogPage> GetAnimationCatalogPageAsync(
        AnimationCatalogCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var readContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await readContext.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var revision = await ReadCatalogRevisionAsync(readContext, cancellationToken);
            if (cursor is not null && cursor.Revision != revision)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AnimationCatalogPage([], null, revision, true);
            }

            var query = readContext.AnimationCatalogEntries.AsNoTracking();
            if (cursor is not null)
            {
                query = query.Where(entry =>
                    entry.LatestPublishTime < cursor.LatestPublishTime
                    || (entry.LatestPublishTime == cursor.LatestPublishTime
                        && string.Compare(entry.TmdbId, cursor.TmdbId) < 0));
            }

            var items = await query
                .OrderByDescending(entry => entry.LatestPublishTime)
                .ThenByDescending(entry => entry.TmdbId)
                .Select(entry => new AnimationCatalogItem(
                    entry.TmdbId,
                    entry.Name,
                    entry.OriginalName,
                    entry.PosterPath,
                    entry.EpisodeCount,
                    entry.ReleaseCount,
                    entry.AutomationAttentionCount,
                    entry.LatestPublishTime))
                .Take(take + 1)
                .ToListAsync(cancellationToken);
            var hasMore = items.Count > take;
            if (hasMore) items.RemoveAt(items.Count - 1);
            var nextCursor = hasMore && items.Count > 0
                ? new AnimationCatalogCursor(
                    items[^1].LatestPublishTime,
                    items[^1].TmdbId,
                    revision)
                : null;
            await transaction.CommitAsync(cancellationToken);
            return new AnimationCatalogPage(items, nextCursor, revision);
        });
    }

    public async Task<AnimationInfoSummaryPage> GetUncategorizedPageAsync(
        AnimationInfoCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var readContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await readContext.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var revision = await ReadCatalogRevisionAsync(readContext, cancellationToken);
            if (cursor is not null && cursor.Revision != revision)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AnimationInfoSummaryPage([], null, revision, true);
            }

            var source = readContext.AnimationInfo
                .AsNoTracking()
                .Where(info => info.MediaLibraryMissingSince == null && info.Animation == null);
            source = ApplyInfoCursor(source, cursor);
            var rows = await ProjectSummaries(source
                    .OrderByDescending(info => info.PublishTime)
                    .ThenByDescending(info => info.Id)
                    .Take(take + 1))
                .ToListAsync(cancellationToken);
            var page = ToSummaryPage(rows, take, revision);
            await transaction.CommitAsync(cancellationToken);
            return page;
        });
    }

    public async Task<AnimationEpisodePage?> GetAnimationEpisodesPageAsync(
        string tmdbId,
        AnimationInfoCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var readContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await readContext.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var revision = await ReadCatalogRevisionAsync(readContext, cancellationToken);
            var animation = await readContext.AnimationCatalogEntries
                .AsNoTracking()
                .Where(entry => entry.TmdbId == tmdbId)
                .Select(entry => new AnimationCatalogItem(
                    entry.TmdbId,
                    entry.Name,
                    entry.OriginalName,
                    entry.PosterPath,
                    entry.EpisodeCount,
                    entry.ReleaseCount,
                    entry.AutomationAttentionCount,
                    entry.LatestPublishTime))
                .SingleOrDefaultAsync(cancellationToken);
            if (animation is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            if (cursor is not null && cursor.Revision != revision)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AnimationEpisodePage(animation, [], null, revision, true);
            }

            var source = readContext.AnimationInfo
                .AsNoTracking()
                .Where(info => info.MediaLibraryMissingSince == null
                               && info.Animation != null
                               && info.Animation.TmdbId == tmdbId);
            source = ApplyInfoCursor(source, cursor);
            var rows = await ProjectSummaries(source
                    .OrderByDescending(info => info.PublishTime)
                    .ThenByDescending(info => info.Id)
                    .Take(take + 1))
                .ToListAsync(cancellationToken);
            var page = ToSummaryPage(rows, take, revision);
            await transaction.CommitAsync(cancellationToken);
            return new AnimationEpisodePage(
                animation,
                page.Items,
                page.NextCursor,
                revision);
        });
    }

    private static IQueryable<AnimationInfoSummary> ProjectSummaries(
        IQueryable<Models.AnimationInfo> query) =>
        query.Select(info => new AnimationInfoSummary(
            info.Id,
            info.Title,
            info.Description,
            info.PublishTime,
            info.IsDownloadTracked,
            info.IsDownloadFinished,
            info.Season,
            info.Episode,
            info.Group == null ? null : info.Group.Name,
            info.Animation == null ? null : info.Animation.Name,
            info.Animation == null ? null : info.Animation.OriginalName,
            info.Animation == null ? null : info.Animation.TmdbId,
            info.Animation == null ? null : info.Animation.PosterPath,
            info.IsAiProcessed,
            info.SourceFeedId,
            info.ReleaseSizeBytes,
            info.AutomationDisposition,
            info.AutomationExplanationJson,
            info.DownloadType == FileDownloadTypes.MediaLibraryImport));

    private static IQueryable<Models.AnimationInfo> ApplyInfoCursor(
        IQueryable<Models.AnimationInfo> query,
        AnimationInfoCursor? cursor)
    {
        if (cursor is null) return query;
        return query.Where(info =>
            info.PublishTime < cursor.PublishTime
            || (info.PublishTime == cursor.PublishTime && info.Id.CompareTo(cursor.Id) < 0));
    }

    private static async Task<long> ReadCatalogRevisionAsync(
        Models.ApplicationContext readContext,
        CancellationToken cancellationToken) =>
        await readContext.AnimationCatalogStates
            .AsNoTracking()
            .Where(state => state.Id == 1)
            .Select(state => state.Revision)
            .SingleAsync(cancellationToken);

    private static AnimationInfoSummaryPage ToSummaryPage(
        List<AnimationInfoSummary> rows,
        int take,
        long revision)
    {
        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var nextCursor = hasMore && rows.Count > 0
            ? new AnimationInfoCursor(rows[^1].PublishTime, rows[^1].Id, revision)
            : null;
        return new AnimationInfoSummaryPage(rows, nextCursor, revision);
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
            .Where(i => i.IsDownloadFinished
                        && i.MediaLibraryMissingSince == null)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync(cancellationToken);
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new PagedResult<AnimationInfo>(data.Select(e => e.ToRecord()).ToList(), totalCount);
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetDownloadedMigrationBatchAsync(
        DateTimeOffset? beforePublishTime,
        Guid? beforeId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        if (beforePublishTime.HasValue != beforeId.HasValue)
            throw new ArgumentException("Both migration cursor values must be supplied together.");

        var query = context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Group)
            .Include(info => info.Animation)
            .Where(info => info.IsDownloadFinished
                           && info.MediaLibraryMissingSince == null);
        if (beforePublishTime is { } publishTime && beforeId is { } id)
            query = query.Where(info =>
                info.PublishTime < publishTime
                || (info.PublishTime == publishTime && info.Id.CompareTo(id) < 0));

        var data = await query
            .OrderByDescending(info => info.PublishTime)
            .ThenByDescending(info => info.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return data.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<AnimationInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        // The returned domain record is later passed back to UpdateAsync. Always load
        // its relationships so a status-only update cannot accidentally clear them.
        var entity = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .FirstOrDefaultAsync(info => info.Id == id, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<AnimationInfo?> FindByIdWithAnimationAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo
            .AsNoTracking()
            .Include(a => a.Animation)
            .Include(a => a.Group)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<AnimationInfo?> FindByStorageLocationAsync(
        string fileStore,
        string storePath,
        CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .FirstOrDefaultAsync(
                info => info.FileStore == fileStore && info.StorePath == storePath,
                cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetByStorageLocationsAsync(
        string fileStore,
        IReadOnlyCollection<string> storePaths,
        CancellationToken cancellationToken)
    {
        if (storePaths.Count == 0) return [];

        var paths = storePaths
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => info.FileStore == fileStore
                           && info.StorePath != null
                           && paths.Contains(info.StorePath))
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetByMediaLibrarySourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => info.MediaLibrarySourceId == sourceId
                           && info.DownloadType == FileDownloadTypes.MediaLibraryImport)
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetUnownedMediaLibraryEntriesUnderPathAsync(
        string fileStore,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var pathPrefix = Path.EndsInDirectorySeparator(sourcePath)
            ? sourcePath
            : sourcePath + Path.DirectorySeparatorChar;
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => info.DownloadType == FileDownloadTypes.MediaLibraryImport
                           && info.MediaLibrarySourceId == null
                           && info.FileStore == fileStore
                           && info.StorePath != null
                           && (info.StorePath == sourcePath
                               || info.StorePath.StartsWith(pathPrefix)))
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetByPhysicalPathsAsync(
        string fileStore,
        IReadOnlyCollection<string> physicalPaths,
        CancellationToken cancellationToken)
    {
        if (physicalPaths.Count == 0) return [];

        var paths = physicalPaths
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var animationInfoIds = context.FileMappings
            .AsNoTracking()
            .Where(mapping => mapping.FileStore == fileStore
                              && paths.Contains(mapping.PhysicalPath))
            .Select(mapping => mapping.AnimationInfoId)
            .Distinct();
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => animationInfoIds.Contains(info.Id))
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<bool> RemoveMediaLibraryEntryAsync(
        Guid id,
        Guid? expectedSourceId,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);

            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null
                || entity.MediaLibrarySourceId != expectedSourceId
                || entity.DownloadType != FileDownloadTypes.MediaLibraryImport)
                return false;
            var previousEpisodeIdentity = GetEpisodeIdentity(writeContext, entity);
            var wasActiveRelease = entity.IsActiveRelease;
            var previousMappings = await writeContext.FileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == entity.Id)
                .OrderBy(mapping => mapping.VirtualPath)
                .ToListAsync(cancellationToken);
            entity.IsActiveRelease = false;
            entity.StateVersion = checked(entity.StateVersion + 1);
            await writeContext.SaveChangesAsync(cancellationToken);
            await PromotePreviousEpisodeSuccessorAsync(
                writeContext,
                entity.Id,
                wasActiveRelease,
                previousEpisodeIdentity,
                currentIdentity: null,
                previousMappings,
                retainChangedReleaseMappings: false,
                cancellationToken);
            await writeContext.ReleaseUpgradeOperations
                .Where(operation => operation.CurrentReleaseId == entity.Id ||
                                    operation.CandidateReleaseId == entity.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await writeContext.FileMappings
                .Where(mapping => mapping.AnimationInfoId == id)
                .ExecuteDeleteAsync(cancellationToken);
            writeContext.AnimationInfo.Remove(entity);
            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async IAsyncEnumerable<AnimationInfo> GetUnfinishedTorrentDownloadsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var info in context.AnimationInfo
                           .Where(i => i.IsDownloadTracked
                                       && !i.IsDownloadFinished
                                       && i.DownloadType == FileDownloadTypes.TorrentDownload)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            yield return info.ToRecord();
        }
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetPendingInferenceAsync(int maxRetryCount, CancellationToken cancellationToken)
    {
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(i => !i.IsAiProcessed
                        && i.AiRetryCount < maxRetryCount
                        && i.MediaLibraryMissingSince == null
                        && i.MetadataStatus == MetadataReviewStatus.Pending)
            .OrderBy(i => i.PublishTime)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetFailedInferenceAsync(
        CancellationToken cancellationToken)
    {
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => !info.IsAiProcessed
                           && info.MediaLibraryMissingSince == null
                           && info.MetadataStatus == MetadataReviewStatus.Failed)
            .OrderBy(info => info.PublishTime)
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetDownloadedWithoutFileMappingsAsync(
        CancellationToken cancellationToken)
    {
        var entities = await context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => info.IsDownloadFinished
                           && info.MediaLibraryMissingSince == null
                           && info.FileStore != null
                           && info.StorePath != null
                           && !context.FileMappings.Any(mapping => mapping.AnimationInfoId == info.Id)
                           && !context.StagedFileMappings.Any(mapping => mapping.AnimationInfoId == info.Id)
                           // A retired incumbent intentionally owns no live/staged
                           // mappings. Keep active releases in the sweep even if a
                           // stale marker survives an interrupted lifecycle change.
                           && (info.IsActiveRelease || !info.IsRetiredRelease))
            .OrderBy(info => info.DownloadEndTime)
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<AnimationInfo?> FindByTitleAsync(string title, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo
            .FirstOrDefaultAsync(i => i.Title == title, cancellationToken);
        return entity?.ToRecord();
    }

    public Task<bool> ExistsReleaseSourceAsync(
        Guid? sourceFeedId,
        string? feedItemGuid,
        string? enclosureId,
        string downloadUrl,
        CancellationToken cancellationToken)
    {
        var normalizedGuid = NormalizeExternalReleaseId(feedItemGuid);
        var normalizedEnclosure = NormalizeExternalReleaseId(enclosureId);
        var normalizedUrl = downloadUrl.Trim();
        return context.AnimationInfo.AsNoTracking().AnyAsync(info =>
                info.DownloadUrl == normalizedUrl ||
                (normalizedGuid != null &&
                 info.SourceFeedId == sourceFeedId &&
                 info.FeedItemGuid == normalizedGuid) ||
                (normalizedEnclosure != null &&
                 info.SourceFeedId == sourceFeedId &&
                 info.EnclosureId == normalizedEnclosure),
            cancellationToken);
    }

    public async Task AddAsync(AnimationInfo info, CancellationToken cancellationToken)
    {
        var entity = info.ToEntity();
        await context.AnimationInfo.AddAsync(entity, cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "UX_AnimationInfo_ReleaseIdentity"
        } && !string.IsNullOrWhiteSpace(info.ReleaseIdentity))
        {
            context.Entry(entity).State = EntityState.Detached;
            throw new DuplicateReleaseException(info.ReleaseIdentity, exception);
        }
    }

    public async Task UpdateAsync(AnimationInfo info, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                             writeContext,
                             info.Id,
                             cancellationToken)
                         ?? throw new InvalidOperationException($"AnimationInfo {info.Id} not found");
            var currentStateVersion = entity.StateVersion;
            if (currentStateVersion != info.StateVersion)
                throw new DbUpdateConcurrencyException(
                    $"AnimationInfo {info.Id} changed from revision {info.StateVersion} to {currentStateVersion}.");
            var previousEpisodeIdentity = GetEpisodeIdentity(writeContext, entity);
            var wasActiveRelease = entity.IsActiveRelease;
            var previousMappings = await writeContext.FileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == entity.Id)
                .OrderBy(mapping => mapping.VirtualPath)
                .ToListAsync(cancellationToken);
            var previousMappingIdentities = await FileMappingSetReconciler.CaptureIdentitiesAsync(
                writeContext,
                previousMappings,
                cancellationToken);

            await ResetTodoStateForTransitionAsync(
                writeContext,
                entity,
                info.MetadataStatus,
                info.AutomationDisposition,
                cancellationToken);
            info.ApplyTo(entity);
            entity.Animation = info.Animation is null
                ? null
                : await writeContext.Animations.FindAsync([info.Animation.Id], cancellationToken);
            entity.Group = info.Group is null
                ? null
                : await writeContext.AnimationGroups.FindAsync([info.Group.Id], cancellationToken);
            writeContext.Entry(entity).Property<Guid?>("AnimationId").CurrentValue = info.Animation?.Id;
            writeContext.Entry(entity).Property<Guid?>("GroupId").CurrentValue = info.Group?.Id;
            await SetEpisodeReleaseActivityAsync(
                writeContext,
                entity,
                willHaveMappings: false,
                cancellationToken);
            await ReconcileMappingVisibilityAfterMetadataChangeAsync(
                writeContext,
                entity,
                cancellationToken);
            var currentEpisodeIdentity = entity.IsActiveRelease
                ? GetEpisodeIdentity(writeContext, entity)
                : null;
            entity.StateVersion = checked(currentStateVersion + 1);

            await writeContext.SaveChangesAsync(cancellationToken);
            await PromotePreviousEpisodeSuccessorAsync(
                writeContext,
                entity.Id,
                wasActiveRelease,
                previousEpisodeIdentity,
                currentEpisodeIdentity,
                previousMappings,
                retainChangedReleaseMappings: true,
                cancellationToken);
            await previousMappingIdentities.RestoreEntryIdentitiesAsync(
                writeContext,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public Task<bool> TryStartDownloadAsync(
        Guid id,
        Guid downloadAttemptId,
        DateTimeOffset startedAt,
        SubscriptionAutomationDisposition? queuedDisposition,
        CancellationToken cancellationToken) =>
        TryStartDownloadCoreAsync(
            id,
            releaseUpgradeOperationId: null,
            downloadAttemptId,
            startedAt,
            queuedDisposition,
            cancellationToken);

    public Task<bool> TryStartUpgradeDownloadAsync(
        Guid id,
        Guid releaseUpgradeOperationId,
        Guid downloadAttemptId,
        DateTimeOffset startedAt,
        SubscriptionAutomationDisposition? queuedDisposition,
        CancellationToken cancellationToken) =>
        TryStartDownloadCoreAsync(
            id,
            releaseUpgradeOperationId,
            downloadAttemptId,
            startedAt,
            queuedDisposition,
            cancellationToken);

    private async Task<bool> TryStartDownloadCoreAsync(
        Guid id,
        Guid? releaseUpgradeOperationId,
        Guid downloadAttemptId,
        DateTimeOffset startedAt,
        SubscriptionAutomationDisposition? queuedDisposition,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null)
                return false;
            if (releaseUpgradeOperationId is { } operationId &&
                !await writeContext.ReleaseUpgradeOperations.AnyAsync(
                    operation => operation.Id == operationId &&
                                 operation.CandidateReleaseId == id &&
                                 operation.Status == ReleaseUpgradeStatus.Downloading,
                    cancellationToken))
                return false;
            if (entity.IsDownloadTracked)
            {
                // A transaction whose commit acknowledgement was lost can be
                // retried safely with the caller-owned attempt identifier.
                // Once cancellation intent is durable, however, the submitter
                // must not resume that attempt while remote cleanup is in flight.
                if (entity.DownloadAttemptId != downloadAttemptId ||
                    entity.DownloadCancellationId is not null)
                    return false;
            }
            else
            {
                var nextDisposition = queuedDisposition
                    ?? entity.AutomationDisposition switch
                    {
                        SubscriptionAutomationDisposition.Notified or
                            SubscriptionAutomationDisposition.PendingConfirmation or
                            SubscriptionAutomationDisposition.AutoDownloadFailed or
                            SubscriptionAutomationDisposition.DownloadCancelled =>
                            SubscriptionAutomationDisposition.ManualDownloadQueued,
                        _ => entity.AutomationDisposition
                    };
                await ResetTodoStateForTransitionAsync(
                    writeContext,
                    entity,
                    entity.MetadataStatus,
                    nextDisposition,
                    cancellationToken);
                entity.IsDownloadTracked = true;
                entity.IsDownloadFinished = false;
                entity.DownloadAttemptId = downloadAttemptId;
                entity.DownloadCancellationId = null;
                entity.DownloadStartTime = startedAt;
                entity.FileStore = null;
                entity.StorePath = null;
                entity.AutomationDisposition = nextDisposition;
                entity.StateVersion = checked(entity.StateVersion + 1);
                await writeContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<bool> TryBeginCancelDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null
                || !entity.IsDownloadTracked
                || entity.DownloadAttemptId != downloadAttemptId)
                return false;

            if (entity.DownloadCancellationId is not null &&
                entity.DownloadCancellationId != cancellationAttemptId)
                return false;

            // If activation acquired the shared mapping lock first, this is a
            // stale cancellation request for a release that is now live. Do
            // not let the caller delete its remote files after activation.
            if (await writeContext.ReleaseUpgradeOperations.AnyAsync(
                    operation => operation.CandidateReleaseId == entity.Id &&
                                 operation.Status == ReleaseUpgradeStatus.Applied,
                    cancellationToken))
                return false;

            if (entity.DownloadCancellationId is null)
            {
                entity.DownloadCancellationId = cancellationAttemptId;
                entity.StateVersion = checked(entity.StateVersion + 1);
            }

            // Persist the cancellation intent and terminate the pending
            // upgrade atomically. Activation uses the same global lock, so it
            // can no longer commit between remote deletion and local finalize.
            var cancelledAt = DateTimeOffset.UtcNow;
            await writeContext.ReleaseUpgradeOperations
                .Where(operation => operation.CandidateReleaseId == entity.Id &&
                                    (operation.Status == ReleaseUpgradeStatus.Downloading ||
                                     operation.Status == ReleaseUpgradeStatus.Verifying))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(operation => operation.Status, ReleaseUpgradeStatus.Failed)
                        .SetProperty(
                            operation => operation.FailureSummary,
                            "Candidate download cancellation was requested before upgrade activation.")
                        .SetProperty(operation => operation.CompletedAt, cancelledAt),
                    cancellationToken);
            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<AnimationInfo?> TryCompleteDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        string fileStore,
        string storePath,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Keep the state transition and materialization in one retryable
            // transaction. If the read fails after the update, the update rolls
            // back and the execution strategy can safely repeat the whole unit.
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null
                || !entity.IsDownloadTracked
                || entity.DownloadAttemptId != downloadAttemptId
                || entity.DownloadCancellationId is not null)
                return null;

            var changed = false;
            if (!entity.IsDownloadFinished)
            {
                entity.IsDownloadFinished = true;
                entity.DownloadEndTime = completedAt;
                entity.FileStore = fileStore;
                entity.StorePath = storePath;
                changed = true;
            }
            else if (!string.Equals(entity.FileStore, fileStore, StringComparison.Ordinal)
                     || !string.Equals(entity.StorePath, storePath, StringComparison.Ordinal))
            {
                // A delayed completion for an older download must not replace
                // the location persisted by a newer completion.
                return null;
            }

            if (entity.AutomationDisposition is
                SubscriptionAutomationDisposition.AutoDownloadQueued or
                SubscriptionAutomationDisposition.ManualDownloadQueued)
            {
                await ResetTodoStateForTransitionAsync(
                    writeContext,
                    entity,
                    entity.MetadataStatus,
                    SubscriptionAutomationDisposition.DownloadCompleted,
                    cancellationToken);
                entity.AutomationDisposition = SubscriptionAutomationDisposition.DownloadCompleted;
                changed = true;
            }

            if (changed)
            {
                entity.StateVersion = checked(entity.StateVersion + 1);
                await writeContext.SaveChangesAsync(cancellationToken);
            }

            await writeContext.Entry(entity)
                .Reference(info => info.Animation)
                .LoadAsync(cancellationToken);
            await writeContext.Entry(entity)
                .Reference(info => info.Group)
                .LoadAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entity.ToRecord();
        });
    }

    public async Task<AnimationInfo?> TryCancelDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        SubscriptionAutomationDisposition? terminalDisposition,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null)
                return null;

            if (entity.IsDownloadTracked)
            {
                if (entity.DownloadAttemptId != downloadAttemptId
                    || entity.DownloadCancellationId is not null)
                    return null;

                entity.IsDownloadTracked = false;
                entity.IsDownloadFinished = false;
                entity.DownloadAttemptId = null;
                entity.DownloadCancellationId = null;
                var nextDisposition = terminalDisposition
                    ?? (entity.AutomationDisposition is
                        SubscriptionAutomationDisposition.AutoDownloadQueued or
                        SubscriptionAutomationDisposition.ManualDownloadQueued or
                        SubscriptionAutomationDisposition.DownloadCompleted
                            ? SubscriptionAutomationDisposition.DownloadCancelled
                            : entity.AutomationDisposition);
                await ResetTodoStateForTransitionAsync(
                    writeContext,
                    entity,
                    entity.MetadataStatus,
                    nextDisposition,
                    cancellationToken);
                entity.AutomationDisposition = nextDisposition;
                entity.StateVersion = checked(entity.StateVersion + 1);
                var cancelledAt = DateTimeOffset.UtcNow;
                await writeContext.ReleaseUpgradeOperations
                    .Where(operation => operation.CandidateReleaseId == entity.Id &&
                                        (operation.Status == ReleaseUpgradeStatus.Downloading ||
                                         operation.Status == ReleaseUpgradeStatus.Verifying))
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(operation => operation.Status, ReleaseUpgradeStatus.Failed)
                            .SetProperty(
                                operation => operation.FailureSummary,
                                "Candidate download was cancelled before upgrade activation.")
                            .SetProperty(operation => operation.CompletedAt, cancelledAt),
                        cancellationToken);
                await writeContext.SaveChangesAsync(cancellationToken);
            }
            else if (entity.DownloadAttemptId is not null
                     || entity.DownloadCancellationId is not null)
            {
                return null;
            }

            await writeContext.Entry(entity)
                .Reference(info => info.Animation)
                .LoadAsync(cancellationToken);
            await writeContext.Entry(entity)
                .Reference(info => info.Group)
                .LoadAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entity.ToRecord();
        });
    }

    public async Task<bool> TryUpdateAsync(
        AnimationInfo info,
        long expectedStateVersion,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                info.Id,
                cancellationToken);
            if (entity is null || entity.StateVersion != expectedStateVersion)
                return false;
            var previousEpisodeIdentity = GetEpisodeIdentity(writeContext, entity);
            var wasActiveRelease = entity.IsActiveRelease;
            var previousMappings = await writeContext.FileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == entity.Id)
                .OrderBy(mapping => mapping.VirtualPath)
                .ToListAsync(cancellationToken);
            var previousMappingIdentities = await FileMappingSetReconciler.CaptureIdentitiesAsync(
                writeContext,
                previousMappings,
                cancellationToken);

            await ResetTodoStateForTransitionAsync(
                writeContext,
                entity,
                info.MetadataStatus,
                info.AutomationDisposition,
                cancellationToken);
            info.ApplyTo(entity);
            entity.Animation = info.Animation is null
                ? null
                : await writeContext.Animations.FindAsync([info.Animation.Id], cancellationToken);
            entity.Group = info.Group is null
                ? null
                : await writeContext.AnimationGroups.FindAsync([info.Group.Id], cancellationToken);
            writeContext.Entry(entity).Property<Guid?>("AnimationId").CurrentValue = info.Animation?.Id;
            writeContext.Entry(entity).Property<Guid?>("GroupId").CurrentValue = info.Group?.Id;
            await SetEpisodeReleaseActivityAsync(
                writeContext,
                entity,
                willHaveMappings: false,
                cancellationToken);
            await ReconcileMappingVisibilityAfterMetadataChangeAsync(
                writeContext,
                entity,
                cancellationToken);
            var currentEpisodeIdentity = entity.IsActiveRelease
                ? GetEpisodeIdentity(writeContext, entity)
                : null;
            entity.StateVersion = checked(expectedStateVersion + 1);
            writeContext.Entry(entity).Property(candidate => candidate.StateVersion).OriginalValue =
                expectedStateVersion;

            await writeContext.SaveChangesAsync(cancellationToken);
            await PromotePreviousEpisodeSuccessorAsync(
                writeContext,
                entity.Id,
                wasActiveRelease,
                previousEpisodeIdentity,
                currentEpisodeIdentity,
                previousMappings,
                retainChangedReleaseMappings: true,
                cancellationToken);
            await previousMappingIdentities.RestoreEntryIdentitiesAsync(
                writeContext,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    internal static async Task SetEpisodeReleaseActivityAsync(
        Models.ApplicationContext writeContext,
        Models.AnimationInfo entity,
        bool willHaveMappings,
        CancellationToken cancellationToken)
    {
        var identity = GetEpisodeIdentity(writeContext, entity);
        var hasMappings = willHaveMappings ||
                          await writeContext.FileMappings.AsNoTracking()
                              .AnyAsync(mapping => mapping.AnimationInfoId == entity.Id, cancellationToken) ||
                          await writeContext.StagedFileMappings.AsNoTracking()
                              .AnyAsync(mapping => mapping.AnimationInfoId == entity.Id, cancellationToken);
        if (identity is null || entity.MediaLibraryMissingSince is not null ||
            !entity.IsDownloadFinished || !hasMappings)
        {
            entity.IsActiveRelease = false;
            return;
        }

        var value = identity.Value;
        var activeOthers = writeContext.AnimationInfo
            .Where(other => other.Id != entity.Id &&
                            other.IsActiveRelease &&
                            EF.Property<Guid?>(other, "AnimationId") == value.AnimationId &&
                            other.Season == value.Season &&
                            other.Episode == value.Episode);
        if (await activeOthers.AsNoTracking().AnyAsync(other =>
                other.MediaLibraryMissingSince == null &&
                other.IsDownloadFinished &&
                writeContext.FileMappings.Any(mapping => mapping.AnimationInfoId == other.Id),
                cancellationToken))
        {
            entity.IsActiveRelease = false;
            return;
        }

        await activeOthers.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(other => other.IsActiveRelease, false)
                .SetProperty(other => other.IsRetiredRelease, true)
                .SetProperty(other => other.StateVersion, other => other.StateVersion + 1),
            cancellationToken);
        entity.IsActiveRelease = true;
        entity.IsRetiredRelease = false;
    }

    internal static EpisodeReleaseIdentity? GetEpisodeIdentity(
        Models.ApplicationContext writeContext,
        Models.AnimationInfo entity)
    {
        writeContext.ChangeTracker.DetectChanges();
        var animationId = entity.Animation?.Id ?? writeContext.Entry(entity)
            .Property<Guid?>("AnimationId")
            .CurrentValue;
        return animationId is { } id && entity.Season is { } season && entity.Episode is { } episode
            ? new EpisodeReleaseIdentity(id, season, episode)
            : null;
    }

    private static async Task ReconcileMappingVisibilityAfterMetadataChangeAsync(
        Models.ApplicationContext writeContext,
        Models.AnimationInfo entity,
        CancellationToken cancellationToken)
    {
        var liveMappings = await writeContext.FileMappings
            .Where(mapping => mapping.AnimationInfoId == entity.Id)
            .ToListAsync(cancellationToken);
        var stagedMappings = await writeContext.StagedFileMappings
            .Where(mapping => mapping.AnimationInfoId == entity.Id)
            .ToListAsync(cancellationToken);
        var shouldStage = GetEpisodeIdentity(writeContext, entity) is not null &&
                          !entity.IsActiveRelease;

        if (shouldStage)
        {
            if (liveMappings.Count == 0) return;

            writeContext.StagedFileMappings.RemoveRange(stagedMappings);
            await writeContext.StagedFileMappings.AddRangeAsync(
                liveMappings.Select(mapping => new Models.StagedFileMapping
                {
                    Id = mapping.Id,
                    AnimationInfoId = mapping.AnimationInfoId,
                    VirtualPath = mapping.VirtualPath,
                    PhysicalPath = mapping.PhysicalPath,
                    FileStore = mapping.FileStore
                }),
                cancellationToken);
            writeContext.FileMappings.RemoveRange(liveMappings);
            return;
        }

        if (liveMappings.Count > 0)
        {
            writeContext.StagedFileMappings.RemoveRange(stagedMappings);
            return;
        }

        if (stagedMappings.Count == 0) return;
        var conflicts = await VirtualPathNamespaceGuard.FindConflictsAsync(
            writeContext,
            entity.Id,
            stagedMappings.Select(mapping => mapping.VirtualPath).ToArray(),
            cancellationToken);
        if (conflicts.Count > 0)
            throw new InvalidOperationException(
                $"Cannot publish the remapped release because '{conflicts[0].OccupiedPath}' is occupied.");

        await writeContext.FileMappings.AddRangeAsync(
            stagedMappings.Select(mapping => new Models.FileMapping
            {
                Id = mapping.Id,
                AnimationInfoId = mapping.AnimationInfoId,
                VirtualPath = mapping.VirtualPath,
                PhysicalPath = mapping.PhysicalPath,
                FileStore = mapping.FileStore
            }),
            cancellationToken);
        writeContext.StagedFileMappings.RemoveRange(stagedMappings);
    }

    internal static async Task PromotePreviousEpisodeSuccessorAsync(
        Models.ApplicationContext writeContext,
        Guid changedReleaseId,
        bool wasActiveRelease,
        EpisodeReleaseIdentity? previousIdentity,
        EpisodeReleaseIdentity? currentIdentity,
        IReadOnlyList<Models.FileMapping> previousMappings,
        bool retainChangedReleaseMappings,
        CancellationToken cancellationToken)
    {
        if (!wasActiveRelease || previousIdentity is not { } previous || previous == currentIdentity)
            return;
        var supersededAt = DateTimeOffset.UtcNow;
        await writeContext.ReleaseUpgradeOperations
            .Where(operation =>
                (operation.CurrentReleaseId == changedReleaseId ||
                 operation.CandidateReleaseId == changedReleaseId) &&
                (operation.Status == ReleaseUpgradeStatus.Downloading ||
                 operation.Status == ReleaseUpgradeStatus.Verifying))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ReleaseUpgradeStatus.Failed)
                    .SetProperty(
                        operation => operation.FailureSummary,
                        "Upgrade was superseded because a referenced release left the episode before activation.")
                    .SetProperty(operation => operation.CompletedAt, supersededAt),
                cancellationToken);
        await writeContext.ReleaseUpgradeOperations
            .Where(operation => operation.CandidateReleaseId == changedReleaseId &&
                                operation.Status == ReleaseUpgradeStatus.Applied)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(operation => operation.Status, ReleaseUpgradeStatus.Completed)
                    .SetProperty(operation => operation.CompletedAt, supersededAt),
                cancellationToken);
        if (previousMappings.Count == 0)
            return;
        if (await writeContext.AnimationInfo.AsNoTracking().AnyAsync(
                info => info.Id != changedReleaseId &&
                        info.IsActiveRelease &&
                        EF.Property<Guid?>(info, "AnimationId") == previous.AnimationId &&
                        info.Season == previous.Season &&
                        info.Episode == previous.Episode,
                cancellationToken))
            return;

        var successorId = await writeContext.AnimationInfo
            .AsNoTracking()
            .Where(info => info.Id != changedReleaseId &&
                           !info.IsActiveRelease &&
                           info.MediaLibraryMissingSince == null &&
                           info.IsDownloadFinished &&
                           (writeContext.FileMappings.Any(mapping => mapping.AnimationInfoId == info.Id) ||
                            writeContext.StagedFileMappings.Any(mapping => mapping.AnimationInfoId == info.Id)) &&
                           EF.Property<Guid?>(info, "AnimationId") == previous.AnimationId &&
                           info.Season == previous.Season &&
                           info.Episode == previous.Episode)
            .OrderByDescending(info => info.ReleaseScore)
            .ThenByDescending(info => info.PublishTime)
            .ThenBy(info => info.Id)
            .Select(info => (Guid?)info.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!successorId.HasValue) return;

        var successor = await MappingTransactionLock.LockAnimationInfoAsync(
            writeContext,
            successorId.Value,
            cancellationToken);
        if (successor is null ||
            successor.IsActiveRelease ||
            successor.MediaLibraryMissingSince is not null ||
            !successor.IsDownloadFinished ||
            GetEpisodeIdentity(writeContext, successor) != previous)
            return;

        var successorLiveMappings = await writeContext.FileMappings
            .AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == successor.Id)
            .OrderBy(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);
        var successorStagedMappings = await writeContext.StagedFileMappings
            .Where(mapping => mapping.AnimationInfoId == successor.Id)
            .OrderBy(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);
        if (successorLiveMappings.Count > 0 && successorStagedMappings.Count > 0)
            return;

        var candidateMappings = successorLiveMappings.Count > 0
            ? successorLiveMappings
            : successorStagedMappings
                .Select(mapping => new Models.FileMapping
                {
                    Id = mapping.Id,
                    AnimationInfoId = mapping.AnimationInfoId,
                    VirtualPath = mapping.VirtualPath,
                    PhysicalPath = mapping.PhysicalPath,
                    FileStore = mapping.FileStore
                })
                .ToList();
        if (candidateMappings.Count == 0) return;

        var replacement = ReleaseUpgradeRepository.BuildCandidateReplacement(
            previousMappings,
            candidateMappings,
            successor.Id);
        var retainedMappings = retainChangedReleaseMappings
            ? await writeContext.FileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == changedReleaseId)
                .OrderBy(mapping => mapping.VirtualPath)
                .ToListAsync(cancellationToken)
            : [];
        var retainedStagedMappings = retainChangedReleaseMappings
            ? await writeContext.StagedFileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == changedReleaseId)
                .OrderBy(mapping => mapping.VirtualPath)
                .Select(mapping => new Models.FileMapping
                {
                    Id = mapping.Id,
                    AnimationInfoId = mapping.AnimationInfoId,
                    VirtualPath = mapping.VirtualPath,
                    PhysicalPath = mapping.PhysicalPath,
                    FileStore = mapping.FileStore
                })
                .ToListAsync(cancellationToken)
            : [];
        var successorPaths = replacement.Mappings
            .Select(mapping => mapping.VirtualPath)
            .ToHashSet(StringComparer.Ordinal);
        var vacatedCandidatePaths = replacement.CandidatePathReplacements
            .Where(pair => !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        var retainedPlaybackRelocations = new Dictionary<
            ReleaseUpgradeRepository.PlaybackLocation,
            ReleaseUpgradeRepository.PlaybackLocation>();
        var retainedPhysicalMappings = retainedMappings.Count > 0
            ? retainedMappings
            : retainedStagedMappings;
        foreach (var pathTarget in PlaybackProgressMappingMigrator.BuildPathTargets(
                     previousMappings,
                     retainedPhysicalMappings))
        {
            if (pathTarget.Value is not { } targetPath) continue;
            retainedPlaybackRelocations[
                new ReleaseUpgradeRepository.PlaybackLocation(
                    changedReleaseId,
                    pathTarget.Key)] =
                new ReleaseUpgradeRepository.PlaybackLocation(
                    changedReleaseId,
                    targetPath);
        }

        var retainedDesiredMappings = new List<Models.FileMapping>(retainedMappings.Count);
        foreach (var mapping in retainedMappings)
        {
            if (successorPaths.Contains(mapping.VirtualPath) &&
                vacatedCandidatePaths.TryGetValue(mapping.VirtualPath, out var vacatedPath))
            {
                retainedDesiredMappings.Add(new Models.FileMapping
                {
                    Id = Guid.NewGuid(),
                    AnimationInfoId = mapping.AnimationInfoId,
                    VirtualPath = vacatedPath,
                    PhysicalPath = mapping.PhysicalPath,
                    FileStore = mapping.FileStore
                });
                retainedPlaybackRelocations[
                    new ReleaseUpgradeRepository.PlaybackLocation(
                        mapping.AnimationInfoId,
                        mapping.VirtualPath)] =
                    new ReleaseUpgradeRepository.PlaybackLocation(
                        mapping.AnimationInfoId,
                        vacatedPath);
            }
            else
            {
                retainedDesiredMappings.Add(mapping);
            }
        }

        var desiredMappings = retainedDesiredMappings
            .Concat(replacement.Mappings)
            .ToList();
        var conflicts = await VirtualPathNamespaceGuard.FindConflictsAsync(
            writeContext,
            [changedReleaseId, successor.Id],
            desiredMappings.Select(mapping => mapping.VirtualPath).ToArray(),
            cancellationToken);
        if (conflicts.Count > 0) return;

        var reconciliation = await FileMappingSetReconciler.ReconcileAcrossOwnersAsync(
            writeContext,
            [changedReleaseId, successor.Id],
            desiredMappings,
            cancellationToken);
        if (successorStagedMappings.Count > 0)
            writeContext.StagedFileMappings.RemoveRange(successorStagedMappings);

        var playbackTransfers = ReleaseUpgradeRepository.BuildActivationPlaybackTransfers(
                changedReleaseId,
                successor.Id,
                replacement)
            .ToDictionary();
        foreach (var relocation in retainedPlaybackRelocations)
            playbackTransfers[relocation.Key] = relocation.Value;
        await ReleaseUpgradeRepository.TransferPlaybackProgressAsync(
            writeContext,
            playbackTransfers,
            cancellationToken);

        if (!retainChangedReleaseMappings)
            await writeContext.AnimationInfo
                .Where(info => info.Id == changedReleaseId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(info => info.IsRetiredRelease, true),
                    cancellationToken);
        successor.IsActiveRelease = true;
        successor.IsRetiredRelease = false;
        successor.StateVersion = checked(successor.StateVersion + 1);
        await writeContext.SaveChangesAsync(cancellationToken);
        await reconciliation.RestoreEntryIdentitiesAsync(writeContext, cancellationToken);
    }

    internal readonly record struct EpisodeReleaseIdentity(Guid AnimationId, int Season, int Episode);

    private static string? NormalizeExternalReleaseId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task ResetTodoStateForTransitionAsync(
        Models.ApplicationContext writeContext,
        Models.AnimationInfo current,
        MetadataReviewStatus nextMetadataStatus,
        SubscriptionAutomationDisposition? nextAutomationDisposition,
        CancellationToken cancellationToken)
    {
        if (current.MetadataStatus != nextMetadataStatus
            && (IsMetadataTodoState(current.MetadataStatus)
                || IsMetadataTodoState(nextMetadataStatus)))
        {
            var metadataState = await writeContext.TodoItemStates
                .FindAsync(["metadata:" + current.Id], cancellationToken);
            if (metadataState is not null)
                writeContext.TodoItemStates.Remove(metadataState);
        }

        if (current.AutomationDisposition != nextAutomationDisposition
            && (IsAutomationTodoState(current.AutomationDisposition)
                || IsAutomationTodoState(nextAutomationDisposition)))
        {
            var automationState = await writeContext.TodoItemStates
                .FindAsync(["automation:" + current.Id], cancellationToken);
            if (automationState is not null)
                writeContext.TodoItemStates.Remove(automationState);
        }
    }

    private static bool IsMetadataTodoState(MetadataReviewStatus status) =>
        status is MetadataReviewStatus.LowConfidence or MetadataReviewStatus.Failed;

    private static bool IsAutomationTodoState(SubscriptionAutomationDisposition? disposition) =>
        disposition is SubscriptionAutomationDisposition.Notified
            or SubscriptionAutomationDisposition.PendingConfirmation
            or SubscriptionAutomationDisposition.AutoDownloadFailed;
}
