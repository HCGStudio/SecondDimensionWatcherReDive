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
        var results = await Task.WhenAll(uniquePaths.Select(async path =>
        {
            try
            {
                return new KeyValuePair<string, FileStoreInfo>?(
                    new KeyValuePair<string, FileStoreInfo>(
                        path,
                        await FileInfoAsync(path, cancellationToken)));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }));
        return results
            .Where(pair => pair.HasValue)
            .Select(pair => pair!.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
    public Task<bool> ExistAsync(string path, CancellationToken cancellationToken);
    IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path);
}
