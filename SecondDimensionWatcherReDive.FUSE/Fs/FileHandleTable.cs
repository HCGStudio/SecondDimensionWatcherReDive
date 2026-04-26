using System.Collections.Concurrent;

namespace SecondDimensionWatcherReDive.FUSE.Fs;

// One open handle per fuse `open` / `release` pair. We only need the path so `read`
// can issue an HTTP request without re-parsing the C string fuse passes back. The
// server is the source of truth for byte ranges; we don't pre-buffer.
internal sealed class FileHandleTable
{
    private readonly ConcurrentDictionary<ulong, OpenFile> _open = new();
    private long _next;

    public ulong Allocate(string virtualPath)
    {
        var id = (ulong)Interlocked.Increment(ref _next);
        _open[id] = new OpenFile(virtualPath);
        return id;
    }

    public bool TryGet(ulong id, out OpenFile file)
    {
        if (id != 0 && _open.TryGetValue(id, out var hit))
        {
            file = hit;
            return true;
        }
        file = default!;
        return false;
    }

    public void Release(ulong id) => _open.TryRemove(id, out _);
}

internal sealed record OpenFile(string VirtualPath);
