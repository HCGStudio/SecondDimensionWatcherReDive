namespace SecondDimensionWatcherReDive.Framework.FileStore;

public record FileStoreInfo(bool IsDirectory, string Path, string FileName);

public interface IFileStore
{
    public string Name { get; }
    public Task<Stream> OpenReadStream(string path);
    public Task<FileStoreInfo> FileInfo(string path);
    public Task<bool> Exist(string path);
    IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path);
}