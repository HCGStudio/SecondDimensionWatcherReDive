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
    FileStoreInfo? FileInfo,
    Guid EntryId = default,
    long Cookie = 0);

public sealed record FileExplorePage(
    IReadOnlyList<FileExploreEntry> Items,
    long Generation,
    long? NextCookie,
    bool CursorIsValid);

public interface IFileExplorer
{
    Task<IReadOnlyList<IFileExploreToken>> EnumerateDirectoryAsync(
        DirectoryToken token,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FileExploreEntry>> GetDirectoryEntriesAsync(
        DirectoryToken token,
        CancellationToken cancellationToken);

    async Task<FileExplorePage?> GetDirectoryEntriesPageAsync(
        DirectoryToken token,
        long? afterCookie,
        int take,
        CancellationToken cancellationToken)
    {
        var entries = await GetDirectoryEntriesAsync(token, cancellationToken);
        if (afterCookie.HasValue && entries.All(entry => entry.Cookie != afterCookie.Value))
            return new FileExplorePage([], 1, null, false);
        var page = entries
            .Where(entry => !afterCookie.HasValue || entry.Cookie > afterCookie.Value)
            .OrderBy(entry => entry.Cookie)
            .Take(take)
            .ToList();
        var hasMore = entries.Any(entry => page.Count > 0 && entry.Cookie > page[^1].Cookie);
        return new FileExplorePage(
            page,
            1,
            hasMore && page.Count > 0 ? page[^1].Cookie : null,
            true);
    }

    Task<Stream> OpenReadStreamAsync(
        FileToken token,
        CancellationToken cancellationToken);
}
