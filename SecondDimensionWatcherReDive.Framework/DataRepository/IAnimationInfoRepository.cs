namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record DownloadCancellationLease(
    Guid Id,
    DateTimeOffset ExpiresAt,
    bool RemoveFile);

public sealed record DownloadSubmissionLease(
    Guid Id,
    DateTimeOffset ExpiresAt);

public sealed record PendingDownloadCancellation(
    Guid AnimationInfoId,
    bool RemoveFile);

public sealed record PendingDownloadSubmission(
    Guid AnimationInfoId,
    Guid DownloadAttemptId);

public interface IAnimationInfoRepository
{
    Task<PagedResult<AnimationInfo>> GetPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<long> GetAnimationCatalogRevisionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(1L);

    Task<AnimationCatalogPage> GetAnimationCatalogPageAsync(
        AnimationCatalogCursor? cursor,
        int take,
        CancellationToken cancellationToken);

    Task<AnimationInfoSummaryPage> GetUncategorizedPageAsync(
        AnimationInfoCursor? cursor,
        int take,
        CancellationToken cancellationToken);

    Task<AnimationEpisodePage?> GetAnimationEpisodesPageAsync(
        string tmdbId,
        AnimationInfoCursor? cursor,
        int take,
        CancellationToken cancellationToken);

    Task<PagedResult<AnimationInfo>> GetDownloadingPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<PagedResult<AnimationInfo>> GetDownloadedPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetDownloadedMigrationBatchAsync(
        DateTimeOffset? beforePublishTime,
        Guid? beforeId,
        int take,
        CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByIdWithAnimationAsync(Guid id, CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByStorageLocationAsync(
        string fileStore,
        string storePath,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetByStorageLocationsAsync(
        string fileStore,
        IReadOnlyCollection<string> storePaths,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetByMediaLibrarySourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetUnownedMediaLibraryEntriesUnderPathAsync(
        string fileStore,
        string sourcePath,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetByPhysicalPathsAsync(
        string fileStore,
        IReadOnlyCollection<string> physicalPaths,
        CancellationToken cancellationToken);

    Task<bool> RemoveMediaLibraryEntryAsync(
        Guid id,
        Guid? expectedSourceId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<AnimationInfo> GetUnfinishedTorrentDownloadsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetPendingInferenceAsync(int maxRetryCount, CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetFailedInferenceAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AnimationInfo>> GetDownloadedWithoutFileMappingsAsync(
        CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByTitleAsync(string title, CancellationToken cancellationToken);

    Task<bool> ExistsReleaseSourceAsync(
        Guid? sourceFeedId,
        string? feedItemGuid,
        string? enclosureId,
        string downloadUrl,
        CancellationToken cancellationToken);

    Task AddAsync(AnimationInfo info, CancellationToken cancellationToken);

    Task UpdateAsync(AnimationInfo info, CancellationToken cancellationToken);

    Task<DownloadSubmissionLease?> TryStartDownloadAsync(
        Guid id,
        Guid downloadAttemptId,
        Guid submissionLeaseId,
        TimeSpan submissionLeaseDuration,
        DateTimeOffset startedAt,
        SubscriptionAutomationDisposition? queuedDisposition,
        CancellationToken cancellationToken);

    Task<bool> TryMarkDownloadSubmittedAsync(
        Guid id,
        Guid downloadAttemptId,
        Guid submissionLeaseId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingDownloadSubmission>> GetPendingDownloadSubmissionsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<bool> TryStartUpgradeDownloadAsync(
        Guid id,
        Guid releaseUpgradeOperationId,
        Guid downloadAttemptId,
        DateTimeOffset startedAt,
        SubscriptionAutomationDisposition? queuedDisposition,
        CancellationToken cancellationToken);

    Task<DownloadCancellationLease?> TryBeginCancelDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        Guid cancellationLeaseId,
        TimeSpan cancellationLeaseDuration,
        bool removeFile,
        bool requireUnfinished,
        SubscriptionAutomationDisposition? terminalDisposition,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingDownloadCancellation>> GetPendingDownloadCancellationsAsync(
        int take,
        CancellationToken cancellationToken);

    Task<AnimationInfo?> TryCompleteDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        string fileStore,
        string storePath,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<AnimationInfo?> TryCancelDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        SubscriptionAutomationDisposition? terminalDisposition,
        CancellationToken cancellationToken);

    Task<bool> TryUpdateAsync(
        AnimationInfo info,
        long expectedStateVersion,
        CancellationToken cancellationToken);
}
