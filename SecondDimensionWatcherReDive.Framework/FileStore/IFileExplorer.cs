using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.FileStore;

public interface IFileExploreToken;

public sealed record FileToken(string Path, string FileName) : IFileExploreToken;

public sealed record DirectoryToken(string Path, string FileName) : IFileExploreToken;

public sealed record FileExploreEntry(
    string Path,
    string FileName,
    bool IsDirectory,
    FileMapping? Mapping,
    FileStoreInfo? FileInfo);

public interface IFileExplorer
{
    Task<IReadOnlyList<IFileExploreToken>> EnumerateDirectoryAsync(
        DirectoryToken token,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileExploreEntry>> GetDirectoryEntriesAsync(
        DirectoryToken token,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadStreamAsync(
        FileToken token,
        CancellationToken cancellationToken);
}
