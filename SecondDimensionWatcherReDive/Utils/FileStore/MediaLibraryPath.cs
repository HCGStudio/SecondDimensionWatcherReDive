namespace SecondDimensionWatcherReDive.Utils.FileStore;

internal static class MediaLibraryPath
{
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));

    /// <summary>
    /// Resolves every existing symbolic-link/reparse-point component, rather than
    /// resolving only the final segment. The returned path is suitable for both
    /// containment checks and persistence as a stable physical location.
    /// </summary>
    public static string ResolveExistingPath(string path)
    {
        var normalized = Normalize(path);
        var root = Path.GetPathRoot(normalized)
                   ?? throw new ArgumentException("The path has no filesystem root.", nameof(path));
        var current = Path.TrimEndingDirectorySeparator(root);
        if (string.IsNullOrEmpty(current)) current = root;

        var relative = Path.GetRelativePath(root, normalized);
        if (relative == ".") return Normalize(root);

        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) == 0) continue;

            FileSystemInfo link = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            var target = link.ResolveLinkTarget(returnFinalTarget: true)
                         ?? throw new IOException($"Could not resolve symbolic link '{current}'.");
            current = Normalize(target.FullName);
        }

        return Normalize(current);
    }

    public static bool PathsOverlap(string first, string second)
    {
        var normalizedFirst = NormalizeForComparison(first);
        var normalizedSecond = NormalizeForComparison(second);
        return IsSameOrChild(normalizedFirst, normalizedSecond)
               || IsSameOrChild(normalizedSecond, normalizedFirst);
    }

    public static bool IsAllowed(string candidate, IEnumerable<string> configuredRoots)
    {
        string resolvedCandidate;
        try
        {
            resolvedCandidate = ResolveExistingPath(candidate);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return false;
        }

        foreach (var configuredRoot in configuredRoots)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot)) continue;

            try
            {
                var resolvedRoot = ResolveExistingPath(configuredRoot);
                if (IsSameOrChild(resolvedRoot, resolvedCandidate)) return true;
            }
            catch (Exception exception) when (IsPathException(exception))
            {
                // Invalid or unavailable roots do not widen the allow-list.
            }
        }

        return false;
    }

    public static bool IsLexicallyAllowed(
        string candidate,
        IEnumerable<string> configuredRoots)
    {
        var normalizedCandidate = Normalize(candidate);
        foreach (var configuredRoot in configuredRoots)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot)) continue;
            try
            {
                var normalizedRoot = Normalize(configuredRoot);
                var resolvedRoot = ResolveExistingPath(configuredRoot);
                if (IsSameOrChild(normalizedRoot, normalizedCandidate)
                    || IsSameOrChild(resolvedRoot, normalizedCandidate))
                    return true;
            }
            catch (Exception exception) when (IsPathException(exception))
            {
                // Invalid or unavailable roots do not widen the allow-list.
            }
        }

        return false;
    }

    public static bool PathEquals(string first, string second) =>
        string.Equals(
            Normalize(first),
            Normalize(second),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string NormalizeForComparison(string path)
    {
        try
        {
            return ResolveExistingPath(path);
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            return Normalize(path);
        }
    }

    private static bool IsSameOrChild(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(parent, candidate, comparison)) return true;

        var relative = Path.GetRelativePath(parent, candidate);
        return !Path.IsPathFullyQualified(relative)
               && !string.Equals(relative, "..", comparison)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", comparison)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", comparison);
    }

    private static bool IsPathException(Exception exception) =>
        exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException;
}
