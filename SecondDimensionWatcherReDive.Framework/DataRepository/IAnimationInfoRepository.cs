namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IAnimationInfoRepository
{
    Task<PagedResult<AnimationInfo>> GetPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<AnimationGroupedResult> GetGroupedAsync(CancellationToken cancellationToken);

    Task<PagedResult<AnimationInfo>> GetDownloadingPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<PagedResult<AnimationInfo>> GetDownloadedPagedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByIdWithAnimationAsync(Guid id, CancellationToken cancellationToken);

    IAsyncEnumerable<AnimationInfo> GetUnfinishedTorrentDownloadsAsync();

    Task<IReadOnlyList<AnimationInfo>> GetPendingInferenceAsync(int maxRetryCount, CancellationToken cancellationToken);

    Task<AnimationInfo?> FindByTitleAsync(string title, CancellationToken cancellationToken);

    Task AddAsync(AnimationInfo info, CancellationToken cancellationToken);

    Task UpdateAsync(AnimationInfo info, CancellationToken cancellationToken);
}
