using Microsoft.EntityFrameworkCore;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Keeps playback state attached to the physical file when metadata review or
/// inference changes its virtual path.
/// </summary>
internal static class PlaybackProgressMappingMigrator
{
    public static async Task MigrateAsync(
        Models.ApplicationContext context,
        Guid animationInfoId,
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings,
        CancellationToken cancellationToken)
    {
        var pathTargets = BuildPathTargets(previousMappings, replacementMappings);
        var allExisting = await context.PlaybackProgresses
            .AsNoTracking()
            .Where(progress => progress.AnimationInfoId == animationInfoId)
            .ToListAsync(cancellationToken);
        if (allExisting.Count == 0) return;

        // A missing media-library item temporarily has no FileMappings. If it is
        // restored under a renamed directory, recover addressable progress from its
        // old virtual source-relative path without guessing when the match is not
        // unique. These orphan targets supplement (but never override) targets based
        // on the previous mapping snapshot.
        foreach (var (source, target) in BuildOrphanPathTargets(
                     allExisting.Select(progress => progress.VirtualPath),
                     pathTargets.Keys,
                     replacementMappings,
                     pathTargets.Values.OfType<string>()))
            pathTargets[source] = target;

        var affectedPaths = pathTargets.Keys
            .Concat(pathTargets.Values.OfType<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (affectedPaths.Length == 0) return;

        var affectedPathSet = affectedPaths.ToHashSet(StringComparer.Ordinal);
        var existing = allExisting
            .Where(progress => affectedPathSet.Contains(progress.VirtualPath))
            .ToList();
        if (existing.Count == 0) return;

        var migrated = Plan(existing, pathTargets);
        await context.PlaybackProgresses
            .Where(progress => progress.AnimationInfoId == animationInfoId
                               && affectedPaths.Contains(progress.VirtualPath))
            .ExecuteDeleteAsync(cancellationToken);
        if (migrated.Count > 0)
            await context.PlaybackProgresses.AddRangeAsync(migrated, cancellationToken);
    }

    internal static IReadOnlyList<Models.PlaybackProgress> Plan(
        IReadOnlyList<Models.PlaybackProgress> existing,
        IReadOnlyDictionary<string, string?> pathTargets)
    {
        return existing
            .Select(progress => new
            {
                Progress = progress,
                Target = pathTargets.TryGetValue(progress.VirtualPath, out var target)
                    ? target
                    : progress.VirtualPath
            })
            .Where(item => item.Target is not null)
            .GroupBy(
                item => (item.Progress.UserId, VirtualPath: item.Target!),
                item => item.Progress)
            .Select(group =>
            {
                // A previous interrupted remap may already have a row at the target.
                // Preserve whichever row represents the latest user action.
                var latest = group
                    .OrderByDescending(progress => progress.UpdatedAt)
                    .ThenByDescending(progress => progress.IsWatched)
                    .First();
                return new Models.PlaybackProgress
                {
                    Id = Guid.NewGuid(),
                    UserId = latest.UserId,
                    AnimationInfoId = latest.AnimationInfoId,
                    VirtualPath = group.Key.VirtualPath,
                    PositionSeconds = latest.PositionSeconds,
                    DurationSeconds = latest.DurationSeconds,
                    IsWatched = latest.IsWatched,
                    UpdatedAt = latest.UpdatedAt,
                    WatchedAt = latest.WatchedAt
                };
            })
            .ToList();
    }

    internal static Dictionary<string, string?> BuildPathTargets(
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings)
    {
        var targets = previousMappings
            .Select(mapping => mapping.VirtualPath)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(path => path, _ => (string?)null, StringComparer.Ordinal);

        // A stable virtual path is the strongest playback identity. In particular,
        // known media keeps its canonical path when the backing directory is renamed.
        ApplyUniqueMatches(
            targets,
            previousMappings,
            replacementMappings,
            mapping => mapping.VirtualPath,
            StringComparer.Ordinal);

        // Metadata remaps normally change only the virtual path. Retain the existing
        // physical-file identity as the next strongest signal.
        ApplyUniqueMatches(
            targets,
            previousMappings,
            replacementMappings,
            mapping => new FileIdentity(mapping.FileStore, mapping.PhysicalPath));

        // Unknown media-library paths include a candidate-root segment:
        // /unknown/Old/Season/E01.mkv. Stripping that segment yields a stable
        // source-relative identity across an Old -> New directory rename.
        ApplyUniqueMatches(
            targets,
            previousMappings,
            replacementMappings,
            TryGetSourceRelativeIdentity);

        // Collision suffixes or a concurrent metadata change can alter the virtual
        // path while the physical tree below a renamed directory remains identical.
        // Use common-directory-relative paths only when they are unique both ways.
        ApplyCommonDirectoryRelativeMatches(
            targets,
            previousMappings,
            replacementMappings);

        // A top-level imported file changes both its candidate-root segment and its
        // filename when renamed. Match the remaining files by an explicit, unique
        // role (for example the sole .mkv, or the sole .zh.srt sidecar) rather than
        // choosing an arbitrary file from a multi-episode set.
        ApplyUniqueMatches(
            targets,
            previousMappings,
            replacementMappings,
            TryGetFileRoleIdentity);

        return targets;
    }

    internal static IReadOnlyDictionary<string, string> BuildOrphanPathTargets(
        IEnumerable<string> progressPaths,
        IEnumerable<string> pathsWithPreviousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings,
        IEnumerable<string>? reservedReplacementPaths = null)
    {
        var excluded = pathsWithPreviousMappings.ToHashSet(StringComparer.Ordinal);
        var orphanPaths = progressPaths
            .Where(path => !excluded.Contains(path))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedReplacementPaths = reservedReplacementPaths?.ToHashSet(StringComparer.Ordinal)
                                   ?? new HashSet<string>(StringComparer.Ordinal);

        var replacementsByVirtualPath = replacementMappings
            .GroupBy(mapping => mapping.VirtualPath, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        foreach (var path in orphanPaths)
        {
            if (!replacementsByVirtualPath.TryGetValue(path, out var replacement)) continue;
            if (usedReplacementPaths.Contains(replacement.VirtualPath)) continue;
            targets[path] = replacement.VirtualPath;
            usedReplacementPaths.Add(replacement.VirtualPath);
        }

        var orphanRelativeGroups = orphanPaths
            .Where(path => !targets.ContainsKey(path))
            .Select(path => (Path: path, RelativePath: TryGetSourceRelativePath(path)))
            .Where(item => item.RelativePath is not null)
            .GroupBy(item => item.RelativePath!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var replacementRelativeGroups = replacementMappings
            .Where(mapping => !usedReplacementPaths.Contains(mapping.VirtualPath))
            .Select(mapping =>
                (Mapping: mapping, RelativePath: TryGetSourceRelativePath(mapping.VirtualPath)))
            .Where(item => item.RelativePath is not null)
            .GroupBy(item => item.RelativePath!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var (relativePath, sources) in orphanRelativeGroups)
        {
            if (sources.Count != 1
                || !replacementRelativeGroups.TryGetValue(relativePath, out var replacements)
                || replacements.Count != 1)
                continue;

            targets[sources[0].Path] = replacements[0].Mapping.VirtualPath;
        }

        usedReplacementPaths.UnionWith(targets.Values);
        var orphanRoleGroups = orphanPaths
            .Where(path => !targets.ContainsKey(path))
            .Select(path => (Path: path, Role: TryGetVirtualFileRole(path)))
            .Where(item => item.Role is not null)
            .GroupBy(item => item.Role!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var replacementRoleGroups = replacementMappings
            .Where(mapping => !usedReplacementPaths.Contains(mapping.VirtualPath))
            .Select(mapping =>
                (Mapping: mapping, Role: TryGetVirtualFileRole(mapping.VirtualPath)))
            .Where(item => item.Role is not null)
            .GroupBy(item => item.Role!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var (role, sources) in orphanRoleGroups)
        {
            if (sources.Count != 1
                || !replacementRoleGroups.TryGetValue(role, out var replacements)
                || replacements.Count != 1)
                continue;

            targets[sources[0].Path] = replacements[0].Mapping.VirtualPath;
        }

        return targets;
    }

    private static void ApplyCommonDirectoryRelativeMatches(
        Dictionary<string, string?> targets,
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings)
    {
        var usedReplacementPaths = targets.Values
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var stores = previousMappings
            .Where(mapping => targets.GetValueOrDefault(mapping.VirtualPath) is null)
            .Select(mapping => mapping.FileStore)
            .Intersect(
                replacementMappings
                    .Where(mapping => !usedReplacementPaths.Contains(mapping.VirtualPath))
                    .Select(mapping => mapping.FileStore),
                StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal);

        foreach (var store in stores)
        {
            var previous = previousMappings
                .Where(mapping => string.Equals(mapping.FileStore, store, StringComparison.Ordinal)
                                  && targets.GetValueOrDefault(mapping.VirtualPath) is null)
                .ToList();
            var replacements = replacementMappings
                .Where(mapping => string.Equals(mapping.FileStore, store, StringComparison.Ordinal)
                                  && !usedReplacementPaths.Contains(mapping.VirtualPath))
                .ToList();
            var previousRoot = TryGetCommonDirectory(previous.Select(mapping => mapping.PhysicalPath));
            var replacementRoot = TryGetCommonDirectory(
                replacements.Select(mapping => mapping.PhysicalPath));
            if (previousRoot is null || replacementRoot is null) continue;

            ApplyUniqueMatches(
                targets,
                previous,
                replacements,
                mapping => TryGetRelativePath(previousRoot, mapping.PhysicalPath),
                mapping => TryGetRelativePath(replacementRoot, mapping.PhysicalPath),
                PathComparer);

            usedReplacementPaths = targets.Values
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    private static void ApplyUniqueMatches<TKey>(
        Dictionary<string, string?> targets,
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings,
        Func<Models.FileMapping, TKey?> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        => ApplyUniqueMatches(
            targets,
            previousMappings,
            replacementMappings,
            keySelector,
            keySelector,
            comparer);

    private static void ApplyUniqueMatches<TKey>(
        Dictionary<string, string?> targets,
        IReadOnlyList<Models.FileMapping> previousMappings,
        IReadOnlyList<Models.FileMapping> replacementMappings,
        Func<Models.FileMapping, TKey?> previousKeySelector,
        Func<Models.FileMapping, TKey?> replacementKeySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        var usedReplacementPaths = targets.Values
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var previousGroups = previousMappings
            .Where(mapping => targets.GetValueOrDefault(mapping.VirtualPath) is null)
            .Select(mapping => (Mapping: mapping, Key: previousKeySelector(mapping)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, comparer)
            .ToDictionary(group => group.Key, group => group.ToList(), comparer);
        var replacementGroups = replacementMappings
            .Where(mapping => !usedReplacementPaths.Contains(mapping.VirtualPath))
            .Select(mapping => (Mapping: mapping, Key: replacementKeySelector(mapping)))
            .Where(item => item.Key is not null)
            .GroupBy(item => item.Key!, comparer)
            .ToDictionary(group => group.Key, group => group.ToList(), comparer);

        foreach (var group in previousGroups)
        {
            var key = group.Key;
            var sources = group.Value;
            if (sources.Count != 1
                || !replacementGroups.TryGetValue(key, out var replacements)
                || replacements.Count != 1)
                continue;

            targets[sources[0].Mapping.VirtualPath] = replacements[0].Mapping.VirtualPath;
        }
    }

    private static SourceRelativeIdentity? TryGetSourceRelativeIdentity(
        Models.FileMapping mapping)
    {
        var relativePath = TryGetSourceRelativePath(mapping.VirtualPath);
        return relativePath is null
            ? null
            : new SourceRelativeIdentity(mapping.FileStore, relativePath);
    }

    private static FileRoleIdentity? TryGetFileRoleIdentity(Models.FileMapping mapping)
    {
        var role = TryGetVirtualFileRole(mapping.VirtualPath);
        return role is null
            ? null
            : new FileRoleIdentity(mapping.FileStore, role.ToUpperInvariant());
    }

    private static string? TryGetVirtualFileRole(string virtualPath)
    {
        const string unknownPrefix = "/unknown/";
        if (!virtualPath.StartsWith(unknownPrefix, StringComparison.Ordinal))
            return null;

        var rootEnd = virtualPath.IndexOf('/', unknownPrefix.Length);
        if (rootEnd < 0 || rootEnd == virtualPath.Length - 1) return null;

        var rootName = virtualPath[unknownPrefix.Length..rootEnd];
        var relativePath = virtualPath[(rootEnd + 1)..];
        if (relativePath.Contains('/')) return null;
        if (!relativePath.StartsWith(rootName, StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = relativePath[rootName.Length..];
        return suffix.StartsWith(".", StringComparison.Ordinal) ? suffix : null;
    }

    private static string? TryGetSourceRelativePath(string virtualPath)
    {
        const string unknownPrefix = "/unknown/";
        if (!virtualPath.StartsWith(unknownPrefix, StringComparison.Ordinal)) return null;

        var rootEnd = virtualPath.IndexOf('/', unknownPrefix.Length);
        return rootEnd < 0 || rootEnd == virtualPath.Length - 1
            ? null
            : virtualPath[(rootEnd + 1)..];
    }

    private static string? TryGetCommonDirectory(IEnumerable<string> physicalPaths)
    {
        try
        {
            var paths = physicalPaths.Select(Path.GetFullPath).ToList();
            if (paths.Count == 0) return null;

            var common = Path.GetDirectoryName(paths[0]);
            if (string.IsNullOrEmpty(common)) return null;
            foreach (var path in paths.Skip(1))
            {
                while (common is not null && !IsSameOrChild(common, path))
                {
                    var parent = Directory.GetParent(common)?.FullName;
                    common = string.Equals(parent, common, PathComparison) ? null : parent;
                }

                if (common is null) return null;
            }

            var root = Path.GetPathRoot(common);
            return root is not null && string.Equals(
                Path.TrimEndingDirectorySeparator(root),
                Path.TrimEndingDirectorySeparator(common),
                PathComparison)
                ? null
                : common;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryGetRelativePath(string root, string physicalPath)
    {
        try
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(physicalPath));
            if (Path.IsPathFullyQualified(relative)
                || relative == ".."
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison)
                || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison))
                return null;
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsSameOrChild(string parent, string candidate)
    {
        var relative = Path.GetRelativePath(parent, candidate);
        return !Path.IsPathFullyQualified(relative)
               && relative != ".."
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", PathComparison);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private sealed record FileIdentity(string FileStore, string PhysicalPath);

    private sealed record SourceRelativeIdentity(string FileStore, string RelativePath);

    private sealed record FileRoleIdentity(string FileStore, string Role);
}
