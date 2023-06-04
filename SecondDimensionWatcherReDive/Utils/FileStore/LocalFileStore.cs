namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class LocalFileStore : IFileStore
{
    public string Name => FileStores.LocalDiskStore;

    public Task<Stream> OpenReadStream(string path)
    {
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task<bool> Rename(string oldName, string newName)
    {
        throw new NotImplementedException();
    }

    public async IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Exists)
        {
            yield return new FileStoreInfo(false, path, fileInfo.Name);
            yield break;
        }

        var directoryInfo = new DirectoryInfo(path);

        if (!directoryInfo.Exists) yield break;
        foreach (var fileSystemInfo in directoryInfo.EnumerateFileSystemInfos())
            yield return new FileStoreInfo((fileSystemInfo.Attributes & FileAttributes.Directory) != 0,
                fileSystemInfo.FullName, fileSystemInfo.Name);

        await Task.CompletedTask;
    }

    public Task<FileStoreInfo> FileInfo(string path)
    {
        var fileAttr = File.GetAttributes(path);
        var isDirectory = (fileAttr & FileAttributes.Directory) != 0;
        FileSystemInfo fileSystemInfo = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
        return Task.FromResult(new FileStoreInfo((fileAttr & FileAttributes.Directory) != 0, fileSystemInfo.FullName,
            fileSystemInfo.Name));
    }

    public Task<bool> Exist(string path)
    {
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }
}