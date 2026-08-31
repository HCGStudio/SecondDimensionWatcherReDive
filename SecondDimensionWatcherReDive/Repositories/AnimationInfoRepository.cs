using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
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

    public async Task<AnimationGroupedResult> GetGroupedAsync(CancellationToken cancellationToken)
    {
        var allItems = await context.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Animation)
            .Include(i => i.Group)
            .Where(i => i.MediaLibraryMissingSince == null)
            .OrderByDescending(i => i.PublishTime)
            .ToListAsync(cancellationToken);

        var categorized = allItems
            .Where(i => i.Animation != null)
            .GroupBy(i => i.Animation!.Id)
            .Select(g =>
            {
                var animation = g.First().Animation!;
                var episodes = g
                    .OrderByDescending(i => i.PublishTime)
                    .ThenByDescending(i => i.Id)
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
                           && !context.FileMappings.Any(mapping => mapping.AnimationInfoId == info.Id))
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

    public async Task AddAsync(AnimationInfo info, CancellationToken cancellationToken)
    {
        await context.AnimationInfo.AddAsync(info.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AnimationInfo info, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationInfo.FindAsync([info.Id], cancellationToken)
                     ?? throw new InvalidOperationException($"AnimationInfo {info.Id} not found");
        var currentStateVersion = entity.StateVersion;
        if (currentStateVersion != info.StateVersion)
            throw new DbUpdateConcurrencyException(
                $"AnimationInfo {info.Id} changed from revision {info.StateVersion} to {currentStateVersion}.");

        await ResetTodoStateForTransitionAsync(
            context,
            entity,
            info.MetadataStatus,
            info.AutomationDisposition,
            cancellationToken);
        info.ApplyTo(entity);

        entity.Animation = info.Animation is null
            ? null
            : await context.Animations.FindAsync([info.Animation.Id], cancellationToken);
        entity.Group = info.Group is null
            ? null
            : await context.AnimationGroups.FindAsync([info.Group.Id], cancellationToken);
        entity.StateVersion = checked(currentStateVersion + 1);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryStartDownloadAsync(
        Guid id,
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
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null)
                return false;
            if (entity.IsDownloadTracked)
            {
                // A transaction whose commit acknowledgement was lost can be
                // retried safely with the caller-owned attempt identifier.
                if (entity.DownloadAttemptId != downloadAttemptId)
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
            var entity = await MappingTransactionLock.LockAnimationInfoAsync(
                writeContext,
                id,
                cancellationToken);
            if (entity is null
                || !entity.IsDownloadTracked
                || entity.DownloadAttemptId != downloadAttemptId)
                return false;

            if (entity.DownloadCancellationId == cancellationAttemptId)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            if (entity.DownloadCancellationId is not null)
                return false;

            entity.DownloadCancellationId = cancellationAttemptId;
            entity.StateVersion = checked(entity.StateVersion + 1);
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
        var entity = await context.AnimationInfo
            .FirstOrDefaultAsync(candidate => candidate.Id == info.Id, cancellationToken);
        if (entity is null || entity.StateVersion != expectedStateVersion)
            return false;

        await ResetTodoStateForTransitionAsync(
            context,
            entity,
            info.MetadataStatus,
            info.AutomationDisposition,
            cancellationToken);
        info.ApplyTo(entity);
        entity.Animation = info.Animation is null
            ? null
            : await context.Animations.FindAsync([info.Animation.Id], cancellationToken);
        entity.Group = info.Group is null
            ? null
            : await context.AnimationGroups.FindAsync([info.Group.Id], cancellationToken);
        entity.StateVersion = checked(expectedStateVersion + 1);
        context.Entry(entity).Property(candidate => candidate.StateVersion).OriginalValue = expectedStateVersion;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The transition may also have staged a todo-state deletion. None
            // of those tracked changes belong to the losing revision.
            context.ChangeTracker.Clear();
            return false;
        }
    }

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
