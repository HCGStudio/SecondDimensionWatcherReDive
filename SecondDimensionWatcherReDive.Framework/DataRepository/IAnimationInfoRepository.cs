namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IAnimationInfoRepository
{
    Task<PagedResult<AnimationInfo>> GetPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<AnimationGroupedResult> GetGroupedAsync(CancellationToken cancellationToken);

    Task<PagedResult<AnimationInfo>> GetDownloadingPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<PagedResult<AnimationInfo>> GetDownloadedPagedAsync(int skip, int take, CancellationToken cancellationToken);

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

    Task AddAsync(AnimationInfo info, CancellationToken cancellationToken);

    Task<bool> TryAddReleaseAsync(AnimationInfo info, CancellationToken cancellationToken);

    Task UpdateAsync(AnimationInfo info, CancellationToken cancellationToken);

    Task<bool> TryStartDownloadAsync(
        Guid id,
        Guid downloadAttemptId,
        DateTimeOffset startedAt,
        SubscriptionAutomationDisposition? queuedDisposition,
        CancellationToken cancellationToken);

    Task<bool> TryBeginCancelDownloadAsync(
        Guid id,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
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
