using System.Collections.Concurrent;

namespace SecondDimensionWatcherReDive.Framework.FileStore;

public record FileStoreInfo(
    bool IsDirectory,
    string Path,
    string FileName,
    long? Length = null,
    DateTimeOffset? LastModifiedUtc = null);

public interface IFileStore
{
    public string Name { get; }
    public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken);
    public Task<FileStoreInfo> FileInfoAsync(string path, CancellationToken cancellationToken);
    public async Task<IReadOnlyDictionary<string, FileStoreInfo>> GetFileInfosAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        var uniquePaths = paths.Distinct(StringComparer.Ordinal).ToArray();
        var results = new ConcurrentDictionary<string, FileStoreInfo>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(
            uniquePaths,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 16
            },
            async (path, itemCancellationToken) =>
            {
                try
                {
                    results[path] = await FileInfoAsync(path, itemCancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Missing or temporarily unavailable physical files are omitted.
                }
            });
        return results;
    }
    public Task<bool> ExistAsync(string path, CancellationToken cancellationToken);
    IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path);
}
