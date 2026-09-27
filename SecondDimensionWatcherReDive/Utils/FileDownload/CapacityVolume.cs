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

    public static DriveInfo? FindDrive(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return DriveInfo.GetDrives().Where(drive => drive.IsReady)
            .Where(drive =>
            {
                var root = Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName);
                var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
                return string.Equals(fullPath, root, comparison) || fullPath.StartsWith(prefix, comparison);
            })
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length).FirstOrDefault();
    }
}
