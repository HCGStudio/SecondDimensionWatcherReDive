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

    Task<FileSystemEntry?> FindFileSystemEntryAsync(
        string virtualPath,
        CancellationToken cancellationToken);

    Task<FileSystemEntry?> FindFileSystemEntryByIdAsync(
        Guid entryId,
        CancellationToken cancellationToken) =>
        Task.FromResult<FileSystemEntry?>(null);

    async Task<FileSystemDirectoryPage?> GetImmediateChildrenPageAsync(
        string parentPath,
        long? afterCookie,
        int take,
        CancellationToken cancellationToken)
    {
        var children = await GetImmediateChildrenAsync(parentPath, cancellationToken);
        if (afterCookie.HasValue && children.All(entry => entry.Cookie != afterCookie.Value))
            return new FileSystemDirectoryPage([], 1, null, false);
        var page = children
            .Where(entry => !afterCookie.HasValue || entry.Cookie > afterCookie.Value)
            .OrderBy(entry => entry.Cookie)
            .Take(take)
            .ToList();
        var hasMore = children.Any(entry => page.Count > 0 && entry.Cookie > page[^1].Cookie);
        return new FileSystemDirectoryPage(
            page,
            1,
            hasMore && page.Count > 0 ? page[^1].Cookie : null,
            true);
    }

    Task<IReadOnlyList<FileSystemEntry>> GetImmediateChildrenAsync(
        string parentPath,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<VirtualPathNamespaceConflict>> FindNamespaceConflictsAsync(
        Guid animationInfoId,
        IReadOnlyCollection<string> proposedPaths,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VirtualPathNamespaceConflict>>([]);

    Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken);

    Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken);

    Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken);

    Task<bool> TryFinalizeDownloadCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        Guid cancellationLeaseId,
        SubscriptionAutomationDisposition? terminalDisposition,
        CancellationToken cancellationToken);

    Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken);
}
