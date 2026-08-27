namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IFileMappingRepository
{
    Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken);

    Task<bool> ReplaceForAnimationInfoAsync(
        Guid animationInfoId,
        string expectedFileStore,
        string expectedStorePath,
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken);

    Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken);

    Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken);

    Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken);

    Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken);

    Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken);
}
