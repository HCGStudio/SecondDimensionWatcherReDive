namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IFileMappingRepository
{
    Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken);

    Task<bool> ReplaceForAnimationInfoAsync(
        Guid animationInfoId,
        long expectedStateVersion,
        string expectedFileStore,
        string expectedStorePath,
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileMapping>> GetForAnimationInfoAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<FileMapping>> GetForAnimationInfosAsync(
        IReadOnlyCollection<Guid> animationInfoIds,
        CancellationToken cancellationToken)
    {
        var mappings = new List<FileMapping>();
        foreach (var animationInfoId in animationInfoIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            mappings.AddRange(await GetForAnimationInfoAsync(
                animationInfoId,
                cancellationToken));
        }

        return mappings;
    }

    Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken);

    Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken);

    Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken);

    Task<bool> TryFinalizeDownloadCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        SubscriptionAutomationDisposition? terminalDisposition,
        CancellationToken cancellationToken);

    Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken);
}
