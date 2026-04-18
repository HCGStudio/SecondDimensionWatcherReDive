namespace SecondDimensionWatcherReDive.Framework.FileStore;

public interface IFileExploreToken;

public sealed record FileToken(string Path, string FileName) : IFileExploreToken;

public sealed record DirectoryToken(string Path, string FileName) : IFileExploreToken;

public interface IFileExplorer
{
    Task<IReadOnlyList<IFileExploreToken>> EnumerateDirectoryAsync(
        DirectoryToken token,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadStreamAsync(
        FileToken token,
        CancellationToken cancellationToken);
}
