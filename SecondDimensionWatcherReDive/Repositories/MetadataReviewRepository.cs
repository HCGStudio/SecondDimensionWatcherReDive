using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using DataFileMapping = SecondDimensionWatcherReDive.Framework.DataRepository.FileMapping;

namespace SecondDimensionWatcherReDive.Repositories;

public class MetadataReviewRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions) : IMetadataReviewRepository
{
    private const int RecentOperationLimit = 10;

    public async Task<MetadataReviewQueuePage> GetQueueAsync(
        MetadataReviewStatus status,
        int skip,
        int take,
        Guid? focusId,
        CancellationToken cancellationToken)
    {
        var queueQuery = context.AnimationInfo
            .AsNoTracking()
            .Include(info => info.Animation)
            .Include(info => info.Group)
            .Where(info => info.MetadataStatus == status)
            .OrderByDescending(info => info.PublishTime);

        var totalCount = await queueQuery.CountAsync(cancellationToken);
        var entities = await queueQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        if (focusId.HasValue && entities.All(info => info.Id != focusId.Value))
        {
            var focused = await context.AnimationInfo
                .AsNoTracking()
                .Include(info => info.Animation)
                .Include(info => info.Group)
                .SingleOrDefaultAsync(
                    info => info.Id == focusId.Value && info.MetadataStatus == status,
                    cancellationToken);
            if (focused is not null)
                entities.Insert(0, focused);
        }
        var animationInfoIds = entities.Select(info => info.Id).ToArray();

        var mappingCounts = animationInfoIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await context.FileMappings
                .AsNoTracking()
                .Where(mapping => animationInfoIds.Contains(mapping.AnimationInfoId))
                .GroupBy(mapping => mapping.AnimationInfoId)
                .Select(group => new { AnimationInfoId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.AnimationInfoId, row => row.Count, cancellationToken);
        if (animationInfoIds.Length > 0)
        {
            var stagedCounts = await context.StagedFileMappings
                .AsNoTracking()
                .Where(mapping => animationInfoIds.Contains(mapping.AnimationInfoId))
                .GroupBy(mapping => mapping.AnimationInfoId)
                .Select(group => new { AnimationInfoId = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            foreach (var row in stagedCounts)
                mappingCounts[row.AnimationInfoId] = mappingCounts.GetValueOrDefault(row.AnimationInfoId) + row.Count;
        }

        var operationIds = entities
            .Where(info => info.CurrentMetadataReviewOperationId.HasValue)
            .Select(info => info.CurrentMetadataReviewOperationId!.Value)
            .Distinct()
            .ToArray();
        var currentOperations = operationIds.Length == 0
            ? new Dictionary<Guid, CurrentOperationRow>()
            : await context.MetadataReviewOperations
                .AsNoTracking()
                .Where(operation => operationIds.Contains(operation.Id))
                .Select(operation => new CurrentOperationRow(
                    operation.Id,
                    operation.State,
                    operation.AppliedAt,
                    operation.AppliedVersion))
                .ToDictionaryAsync(operation => operation.Id, cancellationToken);

        var items = entities.Select(info =>
        {
            CurrentOperationRow? currentOperation = null;
            if (info.CurrentMetadataReviewOperationId is { } operationId)
                currentOperations.TryGetValue(operationId, out currentOperation);

            var canUndo = currentOperation is
            {
                State: MetadataReviewOperationState.Applied,
                AppliedVersion: not null
            } && currentOperation.AppliedVersion == info.StateVersion;
            return new MetadataReviewQueueItem(
                info.ToRecord(),
                mappingCounts.GetValueOrDefault(info.Id),
                info.CurrentMetadataReviewOperationId,
                currentOperation?.AppliedAt,
                canUndo);
        }).ToList();

        var countedStatuses = new[]
        {
            MetadataReviewStatus.Pending,
            MetadataReviewStatus.LowConfidence,
            MetadataReviewStatus.Failed
        };
        var countRows = await context.AnimationInfo
            .AsNoTracking()
            .Where(info => countedStatuses.Contains(info.MetadataStatus))
            .GroupBy(info => info.MetadataStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var countsByStatus = countRows.ToDictionary(row => row.Status, row => row.Count);
        var counts = new MetadataReviewCounts(
            countsByStatus.GetValueOrDefault(MetadataReviewStatus.Pending),
            countsByStatus.GetValueOrDefault(MetadataReviewStatus.LowConfidence),
            countsByStatus.GetValueOrDefault(MetadataReviewStatus.Failed));

        var recentRows = await context.MetadataReviewOperations
            .AsNoTracking()
            .Where(operation => operation.State == MetadataReviewOperationState.Applied
                                && operation.AppliedAt.HasValue
                                && operation.AppliedVersion.HasValue)
            .OrderByDescending(operation => operation.AppliedAt)
            .Take(RecentOperationLimit)
            .Select(operation => new
            {
                operation.Id,
                operation.AnimationInfoId,
                operation.AnimationInfo.Title,
                AppliedAt = operation.AppliedAt!.Value,
                Revision = operation.AppliedVersion!.Value,
                CurrentOperationId = operation.AnimationInfo.CurrentMetadataReviewOperationId,
                CurrentRevision = operation.AnimationInfo.StateVersion
            })
            .ToListAsync(cancellationToken);
        var recentOperations = recentRows
            .Select(row => new MetadataReviewOperationSummary(
                row.Id,
                row.AnimationInfoId,
                row.Title,
                row.AppliedAt,
                row.Revision,
                row.CurrentOperationId == row.Id && row.CurrentRevision == row.Revision))
            .ToList();

        return new MetadataReviewQueuePage(items, totalCount, counts, recentOperations);
    }

    public async Task SavePreviewAsync(
        MetadataReviewPreviewDraft draft,
        CancellationToken cancellationToken)
    {
        if (draft.ExpiresAt <= draft.CreatedAt)
            throw new ArgumentException("A metadata review preview must expire after it is created.", nameof(draft));
        if (draft.ProposedMappings.Any(mapping => mapping.AnimationInfoId != draft.AnimationInfoId))
            throw new ArgumentException(
                "Every proposed mapping must belong to the previewed AnimationInfo.",
                nameof(draft));
        if (draft.ProposedMappings
                .Select(mapping => mapping.VirtualPath)
                .Distinct(StringComparer.Ordinal)
                .Count() != draft.ProposedMappings.Count)
            throw new ArgumentException("A preview cannot contain duplicate virtual paths.", nameof(draft));
        if (!await context.AnimationInfo
                .AsNoTracking()
                .AnyAsync(info => info.Id == draft.AnimationInfoId, cancellationToken))
            throw new InvalidOperationException($"AnimationInfo {draft.AnimationInfoId} not found.");

        var operation = new Models.MetadataReviewOperation
        {
            Id = draft.Id,
            AnimationInfoId = draft.AnimationInfoId,
            State = MetadataReviewOperationState.Draft,
            CreatedAt = draft.CreatedAt,
            ExpiresAt = draft.ExpiresAt,
            BaseVersion = draft.BaseVersion,
            BaseFileStore = draft.BaseFileStore,
            BaseStorePath = draft.BaseStorePath,
            BaseIsDownloadFinished = draft.BaseIsDownloadFinished,
            ProposedAnimationTmdbId = draft.ProposedAnimation.TmdbId,
            ProposedAnimationName = draft.ProposedAnimation.Name,
            ProposedAnimationOriginalName = draft.ProposedAnimation.OriginalName,
            ProposedAnimationPosterPath = draft.ProposedAnimation.PosterPath,
            ProposedDescription = draft.ProposedDescription,
            ProposedSeason = draft.ProposedSeason,
            ProposedEpisode = draft.ProposedEpisode,
            ProposedGroupName = draft.ProposedGroupName,
            MappingSnapshots = draft.ProposedMappings
                .Select(mapping => new Models.MetadataReviewMappingSnapshot
                {
                    Id = Guid.NewGuid(),
                    OperationId = draft.Id,
                    Kind = MetadataReviewMappingKind.Proposed,
                    VirtualPath = mapping.VirtualPath,
                    PhysicalPath = mapping.PhysicalPath,
                    FileStore = mapping.FileStore
                })
                .ToList()
        };
        await context.MetadataReviewOperations.AddAsync(operation, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MetadataReviewMutationResult> ApplyPreviewAsync(
        Guid operationId,
        Guid expectedAnimationInfoId,
        CancellationToken cancellationToken)
    {
        var operationOwner = await context.MetadataReviewOperations
            .AsNoTracking()
            .Where(operation => operation.Id == operationId)
            .Select(operation => (Guid?)operation.AnimationInfoId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!operationOwner.HasValue || operationOwner.Value != expectedAnimationInfoId)
            return Failure(MetadataReviewMutationOutcome.NotFound, operationId);

        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var applyContext = new Models.ApplicationContext(contextOptions);
                await using var transaction = await applyContext.Database
                    .BeginTransactionAsync(cancellationToken);

                await MappingTransactionLock.AcquireAsync(applyContext, cancellationToken);
                var animationInfo = await MappingTransactionLock.LockAnimationInfoAsync(
                    applyContext,
                    expectedAnimationInfoId,
                    cancellationToken);
                var operation = await LockOperationAsync(applyContext, operationId, cancellationToken);
                if (animationInfo is null || operation is null)
                    return Failure(MetadataReviewMutationOutcome.NotFound, operationId);
                if (operation.AnimationInfoId != expectedAnimationInfoId)
                    return Failure(MetadataReviewMutationOutcome.NotFound, operationId);
                if (operation.State == MetadataReviewOperationState.Applied)
                {
                    var currentMappings = await LoadOwnedMappingsAsync(
                        applyContext,
                        animationInfo.Id,
                        cancellationToken);
                    var appliedSnapshots = operation.MappingSnapshots
                        .Where(snapshot => snapshot.Kind == MetadataReviewMappingKind.Proposed)
                        .ToList();
                    if (operation.AppliedVersion.HasValue
                        && operation.AppliedAt.HasValue
                        && animationInfo.CurrentMetadataReviewOperationId == operation.Id
                        && animationInfo.StateVersion == operation.AppliedVersion.Value
                        && MappingSetsMatch(currentMappings, appliedSnapshots))
                        return new MetadataReviewMutationResult(
                            MetadataReviewMutationOutcome.Success,
                            operation.Id,
                            animationInfo.Id,
                            animationInfo.StateVersion,
                            operation.AppliedAt.Value,
                            SnapshotRecords(
                                operation.MappingSnapshots,
                                MetadataReviewMappingKind.Previous,
                                animationInfo.Id),
                            currentMappings.Select(mapping => mapping.ToRecord()).ToList());
                }
                if (operation.State != MetadataReviewOperationState.Draft)
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);

                var appliedAt = DateTimeOffset.UtcNow;
                if (operation.ExpiresAt <= appliedAt)
                {
                    operation.State = MetadataReviewOperationState.Expired;
                    await applyContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return Failure(
                        MetadataReviewMutationOutcome.Expired,
                        operationId,
                        animationInfo.Id);
                }

                if (animationInfo.StateVersion != operation.BaseVersion
                    || animationInfo.FileStore != operation.BaseFileStore
                    || animationInfo.StorePath != operation.BaseStorePath
                    || animationInfo.IsDownloadFinished != operation.BaseIsDownloadFinished)
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);

                var proposedSnapshots = operation.MappingSnapshots
                    .Where(snapshot => snapshot.Kind == MetadataReviewMappingKind.Proposed)
                    .OrderBy(snapshot => snapshot.VirtualPath)
                    .ToList();
                var proposedPaths = proposedSnapshots.Select(snapshot => snapshot.VirtualPath).ToArray();
                if (proposedPaths.Distinct(StringComparer.Ordinal).Count() != proposedPaths.Length
                    || (await VirtualPathNamespaceGuard.FindConflictsAsync(
                        applyContext,
                        animationInfo.Id,
                        proposedPaths,
                        cancellationToken)).Count > 0)
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);

                var existingMappings = await LoadOwnedMappingsAsync(
                    applyContext,
                    animationInfo.Id,
                    cancellationToken);
                var mappingsBefore = existingMappings.Select(mapping => mapping.ToRecord()).ToList();

                var animationInfoEntry = applyContext.Entry(animationInfo);
                var previousEpisodeIdentity = AnimationInfoRepository.GetEpisodeIdentity(
                    applyContext,
                    animationInfo);
                var wasActiveRelease = animationInfo.IsActiveRelease;
                operation.PreviousDescription = animationInfo.Description;
                operation.PreviousAnimationId = animationInfoEntry
                    .Property<Guid?>("AnimationId")
                    .CurrentValue;
                operation.PreviousGroupId = animationInfoEntry
                    .Property<Guid?>("GroupId")
                    .CurrentValue;
                operation.PreviousSeason = animationInfo.Season;
                operation.PreviousEpisode = animationInfo.Episode;
                operation.PreviousMetadataStatus = animationInfo.MetadataStatus;
                operation.PreviousConfidence = animationInfo.MetadataConfidence;
                operation.PreviousLastError = animationInfo.MetadataLastError;
                operation.PreviousIsAiProcessed = animationInfo.IsAiProcessed;
                operation.PreviousAiRetryCount = animationInfo.AiRetryCount;
                operation.PreviousReviewedAt = animationInfo.MetadataReviewedAt;
                operation.PreviousCurrentOperationId = animationInfo.CurrentMetadataReviewOperationId;
                var previousMappingSnapshots = existingMappings
                    .Select(mapping => new Models.MetadataReviewMappingSnapshot
                    {
                        Id = Guid.NewGuid(),
                        OperationId = operation.Id,
                        Operation = operation,
                        Kind = MetadataReviewMappingKind.Previous,
                        VirtualPath = mapping.VirtualPath,
                        PhysicalPath = mapping.PhysicalPath,
                        FileStore = mapping.FileStore
                    })
                    .ToList();
                if (previousMappingSnapshots.Count > 0)
                    await applyContext.MetadataReviewMappingSnapshots.AddRangeAsync(
                        previousMappingSnapshots,
                        cancellationToken);

                var animation = await FindOrCreateAnimationAsync(
                    applyContext,
                    operation,
                    cancellationToken);
                var group = await FindOrCreateGroupAsync(
                    applyContext,
                    operation.ProposedGroupName,
                    cancellationToken);

                await applyContext.TodoItemStates
                    .Where(state => state.Key == "metadata:" + animationInfo.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                animationInfo.Description = operation.ProposedDescription;
                animationInfo.Animation = animation;
                animationInfo.Group = group;
                animationInfoEntry.Property<Guid?>("AnimationId").CurrentValue = animation.Id;
                animationInfoEntry.Property<Guid?>("GroupId").CurrentValue = group?.Id;
                animationInfo.Season = operation.ProposedSeason;
                animationInfo.Episode = operation.ProposedEpisode;
                animationInfo.MetadataStatus = MetadataReviewStatus.Reviewed;
                animationInfo.MetadataConfidence = 1;
                animationInfo.MetadataLastError = null;
                animationInfo.IsAiProcessed = true;
                animationInfo.AiRetryCount = 0;
                animationInfo.MetadataReviewedAt = appliedAt;
                animationInfo.CurrentMetadataReviewOperationId = operation.Id;
                await AnimationInfoRepository.SetEpisodeReleaseActivityAsync(
                    applyContext,
                    animationInfo,
                    willHaveMappings: proposedSnapshots.Count > 0,
                    cancellationToken);
                var currentEpisodeIdentity = animationInfo.IsActiveRelease
                    ? AnimationInfoRepository.GetEpisodeIdentity(applyContext, animationInfo)
                    : null;
                var shouldStage = currentEpisodeIdentity is null &&
                                  AnimationInfoRepository.GetEpisodeIdentity(applyContext, animationInfo) is not null;
                animationInfo.StateVersion = checked(animationInfo.StateVersion + 1);

                var desiredMappings = proposedSnapshots
                    .Select(snapshot => new Models.FileMapping
                    {
                        Id = Guid.NewGuid(),
                        AnimationInfoId = animationInfo.Id,
                        VirtualPath = snapshot.VirtualPath,
                        PhysicalPath = snapshot.PhysicalPath,
                        FileStore = snapshot.FileStore
                    })
                    .ToList();
                await PlaybackProgressMappingMigrator.MigrateAsync(
                    applyContext,
                    animationInfo.Id,
                    existingMappings,
                    desiredMappings,
                    cancellationToken);
                var reconciliation = await FileMappingSetReconciler.ReconcileAsync(
                    applyContext,
                    animationInfo.Id,
                    shouldStage ? [] : desiredMappings,
                    cancellationToken);
                await ReplaceStagedMappingsAsync(
                    applyContext,
                    animationInfo.Id,
                    shouldStage ? desiredMappings : [],
                    cancellationToken);
                var replacementMappings = desiredMappings;

                operation.State = MetadataReviewOperationState.Applied;
                operation.AppliedAt = appliedAt;
                operation.UndoneAt = null;
                operation.AppliedVersion = animationInfo.StateVersion;

                await applyContext.SaveChangesAsync(cancellationToken);
                await AnimationInfoRepository.PromotePreviousEpisodeSuccessorAsync(
                    applyContext,
                    animationInfo.Id,
                    wasActiveRelease,
                    previousEpisodeIdentity,
                    currentEpisodeIdentity,
                    cancellationToken);
                await reconciliation.RestoreEntryIdentitiesAsync(
                    applyContext,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new MetadataReviewMutationResult(
                    MetadataReviewMutationOutcome.Success,
                    operation.Id,
                    animationInfo.Id,
                    animationInfo.StateVersion,
                    appliedAt,
                    mappingsBefore,
                    replacementMappings.Select(mapping => mapping.ToRecord()).ToList());
            });
        }
        catch (DbUpdateException exception) when (IsConstraintConflict(exception))
        {
            return Failure(
                MetadataReviewMutationOutcome.Conflict,
                operationId,
                expectedAnimationInfoId);
        }
    }

    public async Task<MetadataReviewMutationResult> UndoAsync(
        Guid operationId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var operationOwner = await context.MetadataReviewOperations
            .AsNoTracking()
            .Where(operation => operation.Id == operationId)
            .Select(operation => (Guid?)operation.AnimationInfoId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!operationOwner.HasValue)
            return Failure(MetadataReviewMutationOutcome.NotFound, operationId);

        var animationInfoId = operationOwner.Value;
        var strategy = context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                await using var undoContext = new Models.ApplicationContext(contextOptions);
                await using var transaction = await undoContext.Database
                    .BeginTransactionAsync(cancellationToken);

                await MappingTransactionLock.AcquireAsync(undoContext, cancellationToken);
                var animationInfo = await MappingTransactionLock.LockAnimationInfoAsync(
                    undoContext,
                    animationInfoId,
                    cancellationToken);
                var operation = await LockOperationAsync(undoContext, operationId, cancellationToken);
                if (animationInfo is null || operation is null)
                    return Failure(MetadataReviewMutationOutcome.NotFound, operationId);
                if (operation.State == MetadataReviewOperationState.Undone)
                {
                    var idempotentCurrentMappings = await LoadOwnedMappingsAsync(
                        undoContext,
                        animationInfo.Id,
                        cancellationToken);
                    var restoredSnapshots = operation.MappingSnapshots
                        .Where(snapshot => snapshot.Kind == MetadataReviewMappingKind.Previous)
                        .ToList();
                    if (operation.AppliedVersion == expectedVersion
                        && operation.UndoneAt.HasValue
                        && expectedVersion < long.MaxValue
                        && animationInfo.StateVersion == expectedVersion + 1
                        && animationInfo.CurrentMetadataReviewOperationId
                        == operation.PreviousCurrentOperationId
                        && MappingSetsMatch(idempotentCurrentMappings, restoredSnapshots))
                        return new MetadataReviewMutationResult(
                            MetadataReviewMutationOutcome.Success,
                            operation.Id,
                            animationInfo.Id,
                            animationInfo.StateVersion,
                            operation.UndoneAt.Value,
                            SnapshotRecords(
                                operation.MappingSnapshots,
                                MetadataReviewMappingKind.Proposed,
                                animationInfo.Id),
                            idempotentCurrentMappings.Select(mapping => mapping.ToRecord()).ToList());
                }
                if (operation.State != MetadataReviewOperationState.Applied
                    || !operation.AppliedVersion.HasValue
                    || animationInfo.CurrentMetadataReviewOperationId != operation.Id
                    || animationInfo.StateVersion != expectedVersion
                    || operation.AppliedVersion.Value != expectedVersion)
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);
                if (operation.PreviousDescription is null
                    || !operation.PreviousMetadataStatus.HasValue
                    || !operation.PreviousIsAiProcessed.HasValue
                    || !operation.PreviousAiRetryCount.HasValue)
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);

                var currentMappings = await LoadOwnedMappingsAsync(
                    undoContext,
                    animationInfo.Id,
                    cancellationToken);
                var proposedSnapshots = operation.MappingSnapshots
                    .Where(snapshot => snapshot.Kind == MetadataReviewMappingKind.Proposed)
                    .ToList();
                if (!MappingSetsMatch(currentMappings, proposedSnapshots))
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);

                var previousSnapshots = operation.MappingSnapshots
                    .Where(snapshot => snapshot.Kind == MetadataReviewMappingKind.Previous)
                    .OrderBy(snapshot => snapshot.VirtualPath)
                    .ToList();
                var previousPaths = previousSnapshots.Select(snapshot => snapshot.VirtualPath).ToArray();
                if ((await VirtualPathNamespaceGuard.FindConflictsAsync(
                        undoContext,
                        animationInfo.Id,
                        previousPaths,
                        cancellationToken)).Count > 0)
                    return Failure(
                        MetadataReviewMutationOutcome.Conflict,
                        operationId,
                        animationInfo.Id);

                Models.Animation? previousAnimation = null;
                if (operation.PreviousAnimationId is { } previousAnimationId)
                {
                    previousAnimation = await undoContext.Animations.FindAsync(
                        [previousAnimationId],
                        cancellationToken);
                    if (previousAnimation is null)
                        return Failure(
                            MetadataReviewMutationOutcome.Conflict,
                            operationId,
                            animationInfo.Id);
                }

                Models.AnimationGroup? previousGroup = null;
                if (operation.PreviousGroupId is { } previousGroupId)
                {
                    previousGroup = await undoContext.AnimationGroups.FindAsync(
                        [previousGroupId],
                        cancellationToken);
                    if (previousGroup is null)
                        return Failure(
                            MetadataReviewMutationOutcome.Conflict,
                            operationId,
                            animationInfo.Id);
                }

                Models.MetadataReviewOperation? previousOperation = null;
                if (operation.PreviousCurrentOperationId is { } previousOperationId)
                {
                    if (previousOperationId == operation.Id)
                        return Failure(
                            MetadataReviewMutationOutcome.Conflict,
                            operationId,
                            animationInfo.Id);
                    previousOperation = await LockOperationAsync(
                        undoContext,
                        previousOperationId,
                        cancellationToken);
                    if (previousOperation is null
                        || previousOperation.AnimationInfoId != animationInfo.Id)
                        return Failure(
                            MetadataReviewMutationOutcome.Conflict,
                            operationId,
                            animationInfo.Id);
                }

                var previousEpisodeIdentity = AnimationInfoRepository.GetEpisodeIdentity(
                    undoContext,
                    animationInfo);
                var wasActiveRelease = animationInfo.IsActiveRelease;
                await undoContext.TodoItemStates
                    .Where(state => state.Key == "metadata:" + animationInfo.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                animationInfo.Description = operation.PreviousDescription;
                animationInfo.Animation = previousAnimation;
                animationInfo.Group = previousGroup;
                undoContext.Entry(animationInfo).Property<Guid?>("AnimationId").CurrentValue =
                    previousAnimation?.Id;
                undoContext.Entry(animationInfo).Property<Guid?>("GroupId").CurrentValue =
                    previousGroup?.Id;
                animationInfo.Season = operation.PreviousSeason;
                animationInfo.Episode = operation.PreviousEpisode;
                animationInfo.MetadataStatus = operation.PreviousMetadataStatus.Value;
                animationInfo.MetadataConfidence = operation.PreviousConfidence;
                animationInfo.MetadataLastError = operation.PreviousLastError;
                animationInfo.IsAiProcessed = operation.PreviousIsAiProcessed.Value;
                animationInfo.AiRetryCount = operation.PreviousAiRetryCount.Value;
                animationInfo.MetadataReviewedAt = operation.PreviousReviewedAt;
                animationInfo.CurrentMetadataReviewOperationId = operation.PreviousCurrentOperationId;
                await AnimationInfoRepository.SetEpisodeReleaseActivityAsync(
                    undoContext,
                    animationInfo,
                    willHaveMappings: previousSnapshots.Count > 0,
                    cancellationToken);
                var currentEpisodeIdentity = animationInfo.IsActiveRelease
                    ? AnimationInfoRepository.GetEpisodeIdentity(undoContext, animationInfo)
                    : null;
                var shouldStage = currentEpisodeIdentity is null &&
                                  AnimationInfoRepository.GetEpisodeIdentity(undoContext, animationInfo) is not null;
                animationInfo.StateVersion = checked(animationInfo.StateVersion + 1);

                var desiredMappings = previousSnapshots
                    .Select(snapshot => new Models.FileMapping
                    {
                        Id = Guid.NewGuid(),
                        AnimationInfoId = animationInfo.Id,
                        VirtualPath = snapshot.VirtualPath,
                        PhysicalPath = snapshot.PhysicalPath,
                        FileStore = snapshot.FileStore
                    })
                    .ToList();
                await PlaybackProgressMappingMigrator.MigrateAsync(
                    undoContext,
                    animationInfo.Id,
                    currentMappings,
                    desiredMappings,
                    cancellationToken);
                var reconciliation = await FileMappingSetReconciler.ReconcileAsync(
                    undoContext,
                    animationInfo.Id,
                    shouldStage ? [] : desiredMappings,
                    cancellationToken);
                await ReplaceStagedMappingsAsync(
                    undoContext,
                    animationInfo.Id,
                    shouldStage ? desiredMappings : [],
                    cancellationToken);
                var restoredMappings = desiredMappings;

                var undoneAt = DateTimeOffset.UtcNow;
                operation.State = MetadataReviewOperationState.Undone;
                operation.UndoneAt = undoneAt;
                if (previousOperation is
                    {
                        State: MetadataReviewOperationState.Applied,
                        AppliedVersion: not null
                    } && previousOperation.AppliedVersion == operation.BaseVersion)
                    previousOperation.AppliedVersion = animationInfo.StateVersion;

                await undoContext.SaveChangesAsync(cancellationToken);
                await AnimationInfoRepository.PromotePreviousEpisodeSuccessorAsync(
                    undoContext,
                    animationInfo.Id,
                    wasActiveRelease,
                    previousEpisodeIdentity,
                    currentEpisodeIdentity,
                    cancellationToken);
                await reconciliation.RestoreEntryIdentitiesAsync(
                    undoContext,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new MetadataReviewMutationResult(
                    MetadataReviewMutationOutcome.Success,
                    operation.Id,
                    animationInfo.Id,
                    animationInfo.StateVersion,
                    undoneAt,
                    currentMappings.Select(mapping => mapping.ToRecord()).ToList(),
                    restoredMappings.Select(mapping => mapping.ToRecord()).ToList());
            });
        }
        catch (DbUpdateException exception) when (IsConstraintConflict(exception))
        {
            return Failure(MetadataReviewMutationOutcome.Conflict, operationId, animationInfoId);
        }
    }

    private static async Task<Models.MetadataReviewOperation?> LockOperationAsync(
        Models.ApplicationContext operationContext,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await operationContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"MetadataReviewOperations\" WHERE \"Id\" = {operationId} FOR UPDATE",
            cancellationToken);
        return await operationContext.MetadataReviewOperations
            .Include(operation => operation.MappingSnapshots)
            .SingleOrDefaultAsync(operation => operation.Id == operationId, cancellationToken);
    }

    private static async Task<Models.Animation> FindOrCreateAnimationAsync(
        Models.ApplicationContext operationContext,
        Models.MetadataReviewOperation operation,
        CancellationToken cancellationToken)
    {
        var newAnimationId = Guid.NewGuid();
        await operationContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Animations" ("Id", "TmdbId", "Name", "OriginalName", "PosterPath")
            VALUES ({newAnimationId}, {operation.ProposedAnimationTmdbId}, {operation.ProposedAnimationName},
                    {operation.ProposedAnimationOriginalName}, {operation.ProposedAnimationPosterPath})
            ON CONFLICT ("TmdbId") DO NOTHING
            """,
            cancellationToken);
        return await operationContext.Animations
            .SingleAsync(
                animation => animation.TmdbId == operation.ProposedAnimationTmdbId,
                cancellationToken);
    }

    private static async Task<Models.AnimationGroup?> FindOrCreateGroupAsync(
        Models.ApplicationContext operationContext,
        string? proposedGroupName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(proposedGroupName))
            return null;

        var newGroupId = Guid.NewGuid();
        await operationContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "AnimationGroups" ("Id", "Name")
            VALUES ({newGroupId}, {proposedGroupName})
            ON CONFLICT ("Name") DO NOTHING
            """,
            cancellationToken);
        return await operationContext.AnimationGroups
            .SingleAsync(group => group.Name == proposedGroupName, cancellationToken);
    }

    private static async Task<List<Models.FileMapping>> LoadOwnedMappingsAsync(
        Models.ApplicationContext operationContext,
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        var liveMappings = await operationContext.FileMappings
            .AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == animationInfoId)
            .ToListAsync(cancellationToken);
        var stagedMappings = await operationContext.StagedFileMappings
            .AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == animationInfoId)
            .Select(mapping => new Models.FileMapping
            {
                Id = mapping.Id,
                AnimationInfoId = mapping.AnimationInfoId,
                VirtualPath = mapping.VirtualPath,
                PhysicalPath = mapping.PhysicalPath,
                FileStore = mapping.FileStore
            })
            .ToListAsync(cancellationToken);
        return liveMappings
            .Concat(stagedMappings)
            .OrderBy(mapping => mapping.VirtualPath, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task ReplaceStagedMappingsAsync(
        Models.ApplicationContext operationContext,
        Guid animationInfoId,
        IReadOnlyList<Models.FileMapping> desiredMappings,
        CancellationToken cancellationToken)
    {
        var existingMappings = await operationContext.StagedFileMappings
            .Where(mapping => mapping.AnimationInfoId == animationInfoId)
            .ToListAsync(cancellationToken);
        operationContext.StagedFileMappings.RemoveRange(existingMappings);
        if (desiredMappings.Count == 0) return;

        await operationContext.StagedFileMappings.AddRangeAsync(
            desiredMappings.Select(mapping => new Models.StagedFileMapping
            {
                Id = mapping.Id,
                AnimationInfoId = mapping.AnimationInfoId,
                VirtualPath = mapping.VirtualPath,
                PhysicalPath = mapping.PhysicalPath,
                FileStore = mapping.FileStore
            }),
            cancellationToken);
    }

    private static bool MappingSetsMatch(
        IReadOnlyList<Models.FileMapping> mappings,
        IReadOnlyList<Models.MetadataReviewMappingSnapshot> snapshots)
    {
        if (mappings.Count != snapshots.Count)
            return false;

        var mappingKeys = mappings
            .Select(mapping => new MappingKey(
                mapping.VirtualPath,
                mapping.PhysicalPath,
                mapping.FileStore))
            .OrderBy(key => key.VirtualPath, StringComparer.Ordinal)
            .ThenBy(key => key.PhysicalPath, StringComparer.Ordinal)
            .ThenBy(key => key.FileStore, StringComparer.Ordinal);
        var snapshotKeys = snapshots
            .Select(snapshot => new MappingKey(
                snapshot.VirtualPath,
                snapshot.PhysicalPath,
                snapshot.FileStore))
            .OrderBy(key => key.VirtualPath, StringComparer.Ordinal)
            .ThenBy(key => key.PhysicalPath, StringComparer.Ordinal)
            .ThenBy(key => key.FileStore, StringComparer.Ordinal);
        return mappingKeys.SequenceEqual(snapshotKeys);
    }

    private static IReadOnlyList<DataFileMapping> SnapshotRecords(
        IEnumerable<Models.MetadataReviewMappingSnapshot> snapshots,
        MetadataReviewMappingKind kind,
        Guid animationInfoId) =>
        snapshots
            .Where(snapshot => snapshot.Kind == kind)
            .OrderBy(snapshot => snapshot.VirtualPath, StringComparer.Ordinal)
            .Select(snapshot => new DataFileMapping(
                snapshot.Id,
                animationInfoId,
                snapshot.VirtualPath,
                snapshot.PhysicalPath,
                snapshot.FileStore))
            .ToList();

    private static bool IsConstraintConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState is PostgresErrorCodes.UniqueViolation
            or PostgresErrorCodes.ForeignKeyViolation
            or PostgresErrorCodes.CheckViolation;

    private static MetadataReviewMutationResult Failure(
        MetadataReviewMutationOutcome outcome,
        Guid operationId,
        Guid? animationInfoId = null) =>
        new(
            outcome,
            operationId,
            animationInfoId,
            null,
            null,
            Array.Empty<DataFileMapping>(),
            Array.Empty<DataFileMapping>());

    private sealed record CurrentOperationRow(
        Guid Id,
        MetadataReviewOperationState State,
        DateTimeOffset? AppliedAt,
        long? AppliedVersion);

    private sealed record MappingKey(
        string VirtualPath,
        string PhysicalPath,
        string FileStore);
}
