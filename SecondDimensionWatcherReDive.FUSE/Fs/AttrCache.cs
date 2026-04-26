using System.Collections.Concurrent;
using SecondDimensionWatcherReDive.FUSE.Client;

namespace SecondDimensionWatcherReDive.FUSE.Fs;

// TTL caches for stat() and readdir() responses. The point is to keep
// `ls -l <dir>` from hammering the server: that one ls produces a list
// call followed by a stat for every entry, and the stat results are
// already inside the list payload. We cache positives only — negatives
// (404) hit the server every time so newly-uploaded files appear quickly.
internal sealed class AttrCache(TimeSpan ttl)
{
    private readonly ConcurrentDictionary<string, Entry<VfsEntry>> _stats = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Entry<VfsEntry[]>> _lists = new(StringComparer.Ordinal);

    public bool TryGetStat(string path, out VfsEntry? entry)
    {
        if (_stats.TryGetValue(path, out var hit) && hit.ExpiresAt > DateTime.UtcNow)
        {
            entry = hit.Value;
            return true;
        }
        entry = null;
        return false;
    }

    public void PutStat(string path, VfsEntry entry)
        => _stats[path] = new Entry<VfsEntry>(entry, DateTime.UtcNow + ttl);

    public bool TryGetList(string path, out VfsEntry[]? entries)
    {
        if (_lists.TryGetValue(path, out var hit) && hit.ExpiresAt > DateTime.UtcNow)
        {
            entries = hit.Value;
            return true;
        }
        entries = null;
        return false;
    }

    public void PutList(string path, VfsEntry[] entries)
    {
        _lists[path] = new Entry<VfsEntry[]>(entries, DateTime.UtcNow + ttl);
        // Pre-warm stat cache for each child so getattr after readdir is free.
        foreach (var child in entries)
        {
            var childPath = path == "/" ? "/" + child.Name : path + "/" + child.Name;
            PutStat(childPath, child);
        }
    }

    public void Invalidate(string path)
    {
        _stats.TryRemove(path, out _);
        _lists.TryRemove(path, out _);
    }

    private readonly record struct Entry<T>(T Value, DateTime ExpiresAt);
}
