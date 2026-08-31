using Microsoft.EntityFrameworkCore;

namespace SecondDimensionWatcherReDive.Repositories;

internal static class FileMappingSetReconciler
{
    public static async Task<FileMappingReconciliation> ReconcileAsync(
        Models.ApplicationContext context,
        Guid animationInfoId,
        IReadOnlyList<Models.FileMapping> desiredMappings,
        CancellationToken cancellationToken)
    {
        var desiredByPath = desiredMappings.ToDictionary(
            mapping => mapping.VirtualPath,
            StringComparer.Ordinal);
        var existingMappings = await context.FileMappings
            .Where(mapping => mapping.AnimationInfoId == animationInfoId)
            .ToListAsync(cancellationToken);
        var identityPaths = existingMappings
            .SelectMany(mapping => EnumerateEntryPaths(mapping.VirtualPath))
            .Concat(desiredMappings.SelectMany(mapping => EnumerateEntryPaths(mapping.VirtualPath)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var preservedIdentities = await context.FileSystemEntries
            .AsNoTracking()
            .Where(entry => identityPaths.Contains(entry.Path))
            .ToDictionaryAsync(
                entry => entry.Path,
                entry => new FileSystemEntryIdentity(entry.EntryId, entry.IsDirectory),
                StringComparer.Ordinal,
                cancellationToken);
        var reconciled = new List<Models.FileMapping>(desiredMappings.Count);

        foreach (var existing in existingMappings)
        {
            if (!desiredByPath.Remove(existing.VirtualPath, out var desired))
            {
                context.FileMappings.Remove(existing);
                continue;
            }

            existing.PhysicalPath = desired.PhysicalPath;
            existing.FileStore = desired.FileStore;
            reconciled.Add(existing);
        }

        foreach (var desired in desiredByPath.Values)
        {
            await context.FileMappings.AddAsync(desired, cancellationToken);
            reconciled.Add(desired);
        }

        return new FileMappingReconciliation(
            reconciled
                .OrderBy(mapping => mapping.VirtualPath, StringComparer.Ordinal)
                .ToList(),
            preservedIdentities);
    }

    private static IEnumerable<string> EnumerateEntryPaths(string virtualPath)
    {
        yield return virtualPath;
        for (var slash = virtualPath.LastIndexOf('/'); slash > 0; slash = virtualPath.LastIndexOf('/', slash - 1))
            yield return virtualPath[..slash];
    }
}

internal sealed record FileMappingReconciliation(
    IReadOnlyList<Models.FileMapping> Mappings,
    IReadOnlyDictionary<string, FileSystemEntryIdentity> PreservedIdentities)
{
    public async Task RestoreEntryIdentitiesAsync(
        Models.ApplicationContext context,
        CancellationToken cancellationToken)
    {
        if (PreservedIdentities.Count == 0) return;

        var paths = PreservedIdentities.Keys.ToArray();
        var currentEntries = await context.FileSystemEntries
            .Where(entry => paths.Contains(entry.Path))
            .ToListAsync(cancellationToken);
        var changed = false;
        foreach (var entry in currentEntries)
        {
            if (!PreservedIdentities.TryGetValue(entry.Path, out var preserved)
                || preserved.IsDirectory != entry.IsDirectory
                || preserved.EntryId == entry.EntryId)
                continue;

            entry.EntryId = preserved.EntryId;
            changed = true;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);
    }
}

internal readonly record struct FileSystemEntryIdentity(Guid EntryId, bool IsDirectory);
