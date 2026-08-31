using System.Security.Cryptography;
using System.Text;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.IntegrationTest.Helpers;

internal sealed class FakeFileMappingRepository : IFileMappingRepository
{
    private readonly List<FileMapping> _mappings;

    public FakeFileMappingRepository(List<FileMapping> mappings)
    {
        _mappings = mappings;
    }

    public List<string> PrefixCalls { get; } = new();
    public int RootEntriesCalls { get; private set; }
    public List<string> ImmediateChildrenCalls { get; } = new();

    private List<FileMapping> Snapshot() => _mappings.ToList();

    public Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<bool> ReplaceForAnimationInfoAsync(
        Guid animationInfoId,
        long expectedStateVersion,
        string expectedFileStore,
        string expectedStorePath,
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        _mappings.RemoveAll(mapping => mapping.AnimationInfoId == animationInfoId);
        _mappings.AddRange(mappings);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<FileMapping>> GetForAnimationInfoAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<FileMapping>>(
            Snapshot().Where(mapping => mapping.AnimationInfoId == animationInfoId).ToList());

    public Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken)
        => Task.FromResult(Snapshot().FirstOrDefault(m => m.VirtualPath == virtualPath));

    public Task<FileSystemEntry?> FindFileSystemEntryAsync(
        string virtualPath,
        CancellationToken cancellationToken)
    {
        var normalized = virtualPath == "/" ? "/" : virtualPath.TrimEnd('/');
        return Task.FromResult(BuildEntries().GetValueOrDefault(normalized));
    }

    public Task<FileSystemEntry?> FindFileSystemEntryByIdAsync(
        Guid entryId,
        CancellationToken cancellationToken) =>
        Task.FromResult(BuildEntries().Values.FirstOrDefault(entry => entry.EntryId == entryId));

    public Task<IReadOnlyList<FileSystemEntry>> GetImmediateChildrenAsync(
        string parentPath,
        CancellationToken cancellationToken)
    {
        ImmediateChildrenCalls.Add(parentPath);
        var normalized = parentPath == "/" ? "/" : parentPath.TrimEnd('/');
        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(BuildEntries().Values
            .Where(entry => entry.ParentPath == normalized)
            .ToList());
    }

    public Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken)
    {
        PrefixCalls.Add(virtualPathPrefix);
        return Task.FromResult<IReadOnlyList<FileMapping>>(
            Snapshot().Where(m => m.VirtualPath.StartsWith(virtualPathPrefix, StringComparison.Ordinal)).ToList());
    }

    public Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken)
    {
        RootEntriesCalls++;
        var dict = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var m in Snapshot())
        {
            if (m.VirtualPath.Length <= 1 || !m.VirtualPath.StartsWith('/')) continue;
            var nextSlash = m.VirtualPath.IndexOf('/', 1);
            var name = nextSlash < 0
                ? m.VirtualPath[1..]
                : m.VirtualPath[1..nextSlash];
            var isDir = nextSlash > 0;
            if (!dict.TryGetValue(name, out var existing) || (!existing && isDir))
                dict[name] = isDir;
        }

        return Task.FromResult<IReadOnlyList<RootEntry>>(
            dict.Select(kv => new RootEntry(kv.Key, kv.Value)).ToList());
    }

    public Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken)
        => Task.FromResult(Snapshot().Any(m => m.VirtualPath == virtualPath));

    public Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<bool> TryFinalizeDownloadCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        CancellationToken cancellationToken)
    {
        _mappings.RemoveAll(mapping => mapping.AnimationInfoId == animationInfoId);
        return Task.FromResult(true);
    }

    public Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    private static string ParentPath(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    private Dictionary<string, FileSystemEntry> BuildEntries()
    {
        var mappings = Snapshot();
        var descendantCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            for (var parent = ParentPath(mapping.VirtualPath);
                 parent != "/";
                 parent = ParentPath(parent))
            {
                descendantCounts[parent] = descendantCounts.GetValueOrDefault(parent) + 1;
            }
        }

        var entries = descendantCounts.ToDictionary(
            pair => pair.Key,
            pair => CreateEntry(pair.Key, isDirectory: true, pair.Value, mapping: null),
            StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            entries[mapping.VirtualPath] = CreateEntry(
                mapping.VirtualPath,
                isDirectory: false,
                descendantFileCount: 1,
                mapping);
        }

        return entries;
    }

    private static FileSystemEntry CreateEntry(
        string path,
        bool isDirectory,
        int descendantFileCount,
        FileMapping? mapping)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        var entryId = new Guid(hash.AsSpan(0, 16), bigEndian: true);
        var cookieBits = BitConverter.ToUInt64(hash, 16) & long.MaxValue;
        var cookie = cookieBits == 0 ? 1 : (long)cookieBits;
        var slash = path.LastIndexOf('/');
        return new FileSystemEntry(
            entryId,
            path,
            ParentPath(path),
            path[(slash + 1)..],
            isDirectory,
            descendantFileCount,
            cookie,
            mapping);
    }
}

internal sealed class FakeFileExplorer : IFileExplorer
{
    private readonly List<FileMapping> _mappings;
    private readonly IFileStore _store;
    private readonly FakeFileMappingRepository _repository;

    public FakeFileExplorer(List<FileMapping> mappings, IFileStore store, FakeFileMappingRepository repository)
    {
        _mappings = mappings;
        _store = store;
        _repository = repository;
    }

    private List<FileMapping> Snapshot() => _mappings.ToList();

    public async Task<IReadOnlyList<IFileExploreToken>> EnumerateDirectoryAsync(DirectoryToken token, CancellationToken cancellationToken)
    {
        var prefix = token.Path.EndsWith('/') ? token.Path : token.Path + "/";

        if (prefix == "/")
        {
            var roots = await _repository.GetRootEntriesAsync(cancellationToken);
            return roots
                .Select<RootEntry, IFileExploreToken>(r => r.IsDirectory
                    ? new DirectoryToken("/" + r.Name, r.Name)
                    : new FileToken("/" + r.Name, r.Name))
                .ToList();
        }

        var direct = new Dictionary<string, IFileExploreToken>(StringComparer.Ordinal);

        foreach (var m in Snapshot())
        {
            if (!m.VirtualPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rel = m.VirtualPath[prefix.Length..];
            if (rel.Length == 0) continue;

            var slash = rel.IndexOf('/');
            if (slash < 0)
            {
                direct[rel] = new FileToken(m.VirtualPath, rel);
            }
            else
            {
                var name = rel[..slash];
                if (!direct.ContainsKey(name))
                    direct[name] = new DirectoryToken(prefix + name, name);
            }
        }

        return direct.Values.ToList();
    }

    public async Task<IReadOnlyList<FileExploreEntry>> GetDirectoryEntriesAsync(
        DirectoryToken token,
        CancellationToken cancellationToken)
    {
        var nodes = await _repository.GetImmediateChildrenAsync(
            token.Path == "/" ? "/" : token.Path.TrimEnd('/'),
            cancellationToken);
        var results = new List<FileExploreEntry>(nodes.Count);
        foreach (var node in nodes)
        {
            FileStoreInfo? info = null;
            if (node.Mapping is not null)
                info = await _store.FileInfoAsync(node.Mapping.PhysicalPath, cancellationToken);
            results.Add(new FileExploreEntry(
                node.Path,
                node.Name,
                node.IsDirectory,
                node.Mapping,
                info,
                node.EntryId,
                node.Cookie));
        }

        return results;
    }

    public Task<Stream> OpenReadStreamAsync(FileToken token, CancellationToken cancellationToken)
    {
        var mapping = Snapshot().FirstOrDefault(m => m.VirtualPath == token.Path)
                      ?? throw new FileNotFoundException(token.Path);
        return _store.OpenReadStreamAsync(mapping.PhysicalPath, cancellationToken);
    }
}
