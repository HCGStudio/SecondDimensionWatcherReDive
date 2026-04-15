namespace SecondDimensionWatcherReDive.Framework.FileStore;

public record FileStoreInfo(bool IsDirectory, string Path, string FileName);

public interface IFileStore
{
    public string Name { get; }
    public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken);
    public Task<FileStoreInfo> FileInfoAsync(string path, CancellationToken cancellationToken);
    public Task<bool> ExistAsync(string path, CancellationToken cancellationToken);
    IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path);
}