using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class LocalFileStore : IFileStore
{
    public string Name => FileStores.LocalDiskStore;

    public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public async IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists)
        {
            yield return new FileStoreInfo(false, path, fileInfo.Name, fileInfo.Length,
                new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero));
            yield break;
        }

        var directoryInfo = new DirectoryInfo(path);

        if (!directoryInfo.Exists) yield break;
        foreach (var fileSystemInfo in directoryInfo.EnumerateFileSystemInfos())
        {
            var isDirectory = (fileSystemInfo.Attributes & FileAttributes.Directory) != 0;
            long? length = !isDirectory && fileSystemInfo is FileInfo fi ? fi.Length : null;
            yield return new FileStoreInfo(isDirectory, fileSystemInfo.FullName, fileSystemInfo.Name, length,
                new DateTimeOffset(fileSystemInfo.LastWriteTimeUtc, TimeSpan.Zero));
        }

        await Task.CompletedTask;
    }

    public Task<FileStoreInfo> FileInfoAsync(string path, CancellationToken cancellationToken)
    {
        var fileAttr = File.GetAttributes(path);
        var isDirectory = (fileAttr & FileAttributes.Directory) != 0;
        FileSystemInfo fileSystemInfo = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        long? length = !isDirectory && fileSystemInfo is FileInfo fi ? fi.Length : null;
        return Task.FromResult(new FileStoreInfo(isDirectory, fileSystemInfo.FullName, fileSystemInfo.Name, length,
            new DateTimeOffset(fileSystemInfo.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    public Task<bool> ExistAsync(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }
}
