using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

internal static class VirtualPathNamespaceGuard
{
    public static bool IsCanonical(string path)
    {
        if (path.Length < 2 || path[0] != '/' || path[^1] == '/') return false;

        foreach (var segment in path.AsSpan(1).ToString().Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..") return false;
        }

        return true;
    }

    public static async Task<IReadOnlyList<VirtualPathNamespaceConflict>> FindConflictsAsync(
        Models.ApplicationContext context,
        Guid animationInfoId,
        IReadOnlyCollection<string> proposedPaths,
        CancellationToken cancellationToken)
    {
        var requested = proposedPaths
            .Order(StringComparer.Ordinal)
            .ToArray();
        var proposed = requested.Distinct(StringComparer.Ordinal).ToArray();
        if (proposed.Any(path => !IsCanonical(path)))
            throw new ArgumentException("Every virtual path must be absolute and canonical.", nameof(proposedPaths));

        var conflicts = new List<VirtualPathNamespaceConflict>();
        foreach (var duplicate in requested
                     .GroupBy(path => path, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            conflicts.Add(new VirtualPathNamespaceConflict(
                duplicate,
                duplicate,
                VirtualPathConflictKind.ProposedPrefix));
        }

        for (var index = 0; index < proposed.Length; index++)
        {
            for (var otherIndex = index + 1; otherIndex < proposed.Length; otherIndex++)
            {
                var left = proposed[index];
                var right = proposed[otherIndex];
                if (IsAncestor(left, right))
                {
                    conflicts.Add(new VirtualPathNamespaceConflict(
                        right,
                        left,
                        VirtualPathConflictKind.ProposedPrefix));
                }
                else if (IsAncestor(right, left))
                {
                    conflicts.Add(new VirtualPathNamespaceConflict(
                        left,
                        right,
                        VirtualPathConflictKind.ProposedPrefix));
                }
            }
        }

        if (proposed.Length == 0) return conflicts;

        var pathsToResolve = proposed
            .SelectMany(path => EnumerateAncestors(path).Append(path))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nodes = await context.FileSystemEntries
            .AsNoTracking()
            .Where(entry => pathsToResolve.Contains(entry.Path))
            .Select(entry => new NamespaceNode(
                entry.Path,
                entry.IsDirectory,
                entry.DescendantFileCount,
                entry.FileMapping == null ? null : entry.FileMapping.AnimationInfoId))
            .ToDictionaryAsync(entry => entry.Path, StringComparer.Ordinal, cancellationToken);
        var ownedPaths = await context.FileMappings
            .AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == animationInfoId)
            .Select(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);

        foreach (var path in proposed)
        {
            foreach (var ancestor in EnumerateAncestors(path))
            {
                if (nodes.TryGetValue(ancestor, out var ancestorNode)
                    && !ancestorNode.IsDirectory
                    && ancestorNode.AnimationInfoId != animationInfoId)
                {
                    conflicts.Add(new VirtualPathNamespaceConflict(
                        path,
                        ancestor,
                        VirtualPathConflictKind.AncestorFile));
                    break;
                }
            }

            if (!nodes.TryGetValue(path, out var exactNode)) continue;
            if (!exactNode.IsDirectory)
            {
                if (exactNode.AnimationInfoId != animationInfoId)
                {
                    conflicts.Add(new VirtualPathNamespaceConflict(
                        path,
                        path,
                        VirtualPathConflictKind.ExistingFile));
                }

                continue;
            }

            var ownedDescendantCount = ownedPaths.Count(ownedPath => IsAncestor(path, ownedPath));
            if (exactNode.DescendantFileCount > ownedDescendantCount)
            {
                conflicts.Add(new VirtualPathNamespaceConflict(
                    path,
                    path,
                    VirtualPathConflictKind.DescendantDirectory));
            }
        }

        return conflicts
            .Distinct()
            .OrderBy(conflict => conflict.ProposedPath, StringComparer.Ordinal)
            .ThenBy(conflict => conflict.OccupiedPath, StringComparer.Ordinal)
            .ToList();
    }

    public static bool IsAncestor(string ancestor, string descendant) =>
        descendant.Length > ancestor.Length
        && descendant.StartsWith(ancestor, StringComparison.Ordinal)
        && descendant[ancestor.Length] == '/';

    public static IEnumerable<string> EnumerateAncestors(string path)
    {
        for (var index = path.IndexOf('/', 1); index >= 0; index = path.IndexOf('/', index + 1))
            yield return path[..index];
    }

    private sealed record NamespaceNode(
        string Path,
        bool IsDirectory,
        int DescendantFileCount,
        Guid? AnimationInfoId);
}
