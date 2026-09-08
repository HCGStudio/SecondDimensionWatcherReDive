namespace SecondDimensionWatcherReDive.Utils.FileDownload;

internal static class CapacityVolume
{
    public static string CanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var directory = new DirectoryInfo(current);
            if (directory.LinkTarget is not null)
                current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                          ?? throw new IOException("The capacity volume contains an unresolved symbolic link.");
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    // Coordination identities are persisted, never passed to filesystem I/O.
    // Fold on every host because case-insensitive volumes also exist on Unix.
    // Case-sensitive paths differing only by case share a conservative lock.
    public static string DirectoryIdentity(string path) => NormalizeDirectoryIdentity(CanonicalPath(path));

    public static string NormalizeDirectoryIdentity(string canonicalPath) =>
        canonicalPath.Replace('\\', '/').TrimEnd('/').ToUpperInvariant();

    public static string? Identity(string path)
    {
        try
        {
            var canonical = CanonicalPath(path);
            if (!OperatingSystem.IsLinux())
                return DownloadCapacityService.FindDrive(canonical)?.Name;
            // Device IDs identify a filesystem across bind mounts. Mount paths alone do not.
            return File.ReadLines("/proc/self/mountinfo")
                .Select(line => line.Split(' '))
                .Where(fields => fields.Length > 5)
                .Select(fields => new { Device = fields[2], Root = Decode(fields[4]) })
                .Where(mount => Contains(mount.Root, canonical))
                .OrderByDescending(mount => mount.Root.Length)
                .Select(mount => "linux:" + mount.Device)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static bool MayShare(string? first, string? second) =>
        first is null || second is null || string.Equals(first, second, StringComparison.Ordinal);

    private static bool Contains(string parent, string path) =>
        path == Path.TrimEndingDirectorySeparator(parent)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal)
        || parent == Path.DirectorySeparatorChar.ToString();

    private static string Decode(string path) => path.Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal).Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);
}
