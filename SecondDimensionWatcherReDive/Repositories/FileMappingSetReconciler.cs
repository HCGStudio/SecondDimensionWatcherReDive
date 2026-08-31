using Microsoft.EntityFrameworkCore;

namespace SecondDimensionWatcherReDive.Repositories;

internal static class FileMappingSetReconciler
{
    public static async Task<FileMappingReconciliation> ReconcileAsync(
        Models.ApplicationContext context,
        Guid animationInfoId,
        IReadOnlyList<Models.FileMapping> desiredMappings,
        CancellationToken cancellationToken) =>
        await ReconcileAcrossOwnersAsync(
            context,
            [animationInfoId],
            desiredMappings,
            cancellationToken);

    public static async Task<FileMappingReconciliation> CaptureIdentitiesAsync(
        Models.ApplicationContext context,
        IReadOnlyList<Models.FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        var identityPaths = mappings
            .SelectMany(mapping => EnumerateEntryPaths(mapping.VirtualPath))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var preservedIdentities = await LoadEntryIdentitiesAsync(
            context,
            identityPaths,
            cancellationToken);
        return new FileMappingReconciliation(mappings, preservedIdentities);
    }

    public static async Task<FileMappingReconciliation> ReconcileAcrossOwnersAsync(
        Models.ApplicationContext context,
        IReadOnlyCollection<Guid> animationInfoIds,
        IReadOnlyList<Models.FileMapping> desiredMappings,
        CancellationToken cancellationToken)
    {
        if (animationInfoIds.Count == 0)
            throw new ArgumentException("At least one mapping owner is required.", nameof(animationInfoIds));
        var ownerIds = animationInfoIds.ToHashSet();
        if (desiredMappings.Any(mapping => !ownerIds.Contains(mapping.AnimationInfoId)))
            throw new ArgumentException("A desired mapping belongs to an owner outside the reconciliation set.",
                nameof(desiredMappings));

        var desiredByPath = desiredMappings.ToDictionary(
            mapping => mapping.VirtualPath,
            StringComparer.Ordinal);
        var existingMappings = await context.FileMappings
            .Where(mapping => ownerIds.Contains(mapping.AnimationInfoId))
            .ToListAsync(cancellationToken);
        var identityPaths = existingMappings
            .SelectMany(mapping => EnumerateEntryPaths(mapping.VirtualPath))
            .Concat(desiredMappings.SelectMany(mapping => EnumerateEntryPaths(mapping.VirtualPath)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var preservedIdentities = await LoadEntryIdentitiesAsync(
            context,
            identityPaths,
            cancellationToken);
        var reconciled = new List<Models.FileMapping>(desiredMappings.Count);
        var hasRemovals = false;

        foreach (var existing in existingMappings)
        {
            if (!desiredByPath.Remove(existing.VirtualPath, out var desired))
            {
                context.FileMappings.Remove(existing);
                hasRemovals = true;
                continue;
            }

            existing.AnimationInfoId = desired.AnimationInfoId;
            existing.PhysicalPath = desired.PhysicalPath;
            existing.FileStore = desired.FileStore;
            reconciled.Add(existing);
        }

        // Apply removals before additions. Some valid remaps replace a file with
        // a directory rooted at the same path (or the reverse), and the hierarchy
        // trigger must observe the old namespace as gone before creating the new one.
        // Callers hold the mapping transaction lock, so this intermediate flush is
        // still atomic with the final reconciliation commit.
        if (hasRemovals)
            await context.SaveChangesAsync(cancellationToken);

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

    private static async Task<IReadOnlyDictionary<string, FileSystemEntryIdentity>> LoadEntryIdentitiesAsync(
        Models.ApplicationContext context,
        IReadOnlyCollection<string> identityPaths,
        CancellationToken cancellationToken) =>
        await context.FileSystemEntries
            .AsNoTracking()
            .Where(entry => identityPaths.Contains(entry.Path))
            .ToDictionaryAsync(
                entry => entry.Path,
                entry => new FileSystemEntryIdentity(entry.EntryId, entry.IsDirectory),
                StringComparer.Ordinal,
                cancellationToken);
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
