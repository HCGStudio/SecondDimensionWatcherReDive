using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using System.Data;

namespace SecondDimensionWatcherReDive.Repositories;

public class FileMappingRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions) : IFileMappingRepository
{
    public async Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken)
    {
        if (mappings.Count == 0) return;

        var allProposedPaths = mappings.Select(mapping => mapping.VirtualPath).ToArray();
        for (var index = 0; index < allProposedPaths.Length; index++)
        {
            if (!VirtualPathNamespaceGuard.IsCanonical(allProposedPaths[index]))
                throw new ArgumentException("Every virtual path must be absolute and canonical.", nameof(mappings));
            for (var otherIndex = index + 1; otherIndex < allProposedPaths.Length; otherIndex++)
            {
                if (string.Equals(
                        allProposedPaths[index],
                        allProposedPaths[otherIndex],
                        StringComparison.Ordinal)
                    || VirtualPathNamespaceGuard.IsAncestor(
                        allProposedPaths[index],
                        allProposedPaths[otherIndex])
                    || VirtualPathNamespaceGuard.IsAncestor(
                        allProposedPaths[otherIndex],
                        allProposedPaths[index]))
                    throw new InvalidOperationException(
                        "The proposed mapping batch contains conflicting virtual paths.");
            }
        }

        var animationInfoIds = mappings
            .Select(mapping => mapping.AnimationInfoId)
            .Distinct()
            .ToArray();
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);

            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var animationInfos = await MappingTransactionLock.LockAnimationInfosAsync(
                writeContext,
                animationInfoIds,
                cancellationToken);
            if (animationInfos.Count != animationInfoIds.Length)
                throw new InvalidOperationException("Cannot add mappings for a missing AnimationInfo.");

            foreach (var animationInfoId in animationInfoIds)
            {
                var conflicts = await VirtualPathNamespaceGuard.FindConflictsAsync(
                    writeContext,
                    animationInfoId,
                    mappings
                        .Where(mapping => mapping.AnimationInfoId == animationInfoId)
                        .Select(mapping => mapping.VirtualPath)
                        .ToArray(),
                    cancellationToken);
                if (conflicts.Count > 0)
                    throw new DbUpdateException(
                        $"The proposed mappings conflict with the virtual-path namespace at '{conflicts[0].OccupiedPath}'.");
            }

            await writeContext.FileMappings.AddRangeAsync(
                mappings.Select(mapping => mapping.ToEntity()),
                cancellationToken);
            foreach (var animationInfo in animationInfos.Values)
                animationInfo.StateVersion = checked(animationInfo.StateVersion + 1);

            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public async Task<bool> ReplaceForAnimationInfoAsync(
        Guid animationInfoId,
        long expectedStateVersion,
        string expectedFileStore,
        string expectedStorePath,
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        if (mappings.Any(mapping => mapping.AnimationInfoId != animationInfoId))
            throw new ArgumentException(
                "Every replacement mapping must belong to the requested AnimationInfo.",
                nameof(mappings));

        var proposedPaths = mappings.Select(mapping => mapping.VirtualPath).ToArray();
        if (proposedPaths.Distinct(StringComparer.Ordinal).Count() != proposedPaths.Length)
            return false;

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Use a fresh context for every execution-strategy retry so entity states from
            // an aborted transaction cannot leak into the next attempt.
            await using var replaceContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await replaceContext.Database
                .BeginTransactionAsync(cancellationToken);

            await MappingTransactionLock.AcquireAsync(replaceContext, cancellationToken);
            var current = await MappingTransactionLock.LockAnimationInfoAsync(
                replaceContext,
                animationInfoId,
                cancellationToken);
            if (current is null
                || current.StateVersion != expectedStateVersion
                || !current.IsDownloadFinished
                || current.FileStore != expectedFileStore
                || current.StorePath != expectedStorePath)
                return false;

            if ((await VirtualPathNamespaceGuard.FindConflictsAsync(
                    replaceContext,
                    animationInfoId,
                    proposedPaths,
                    cancellationToken)).Count > 0)
                return false;

            var existingMappings = await replaceContext.FileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == animationInfoId)
                .ToListAsync(cancellationToken);
            var replacementMappings = mappings
                .Select(mapping => mapping.ToEntity())
                .ToList();
            await PlaybackProgressMappingMigrator.MigrateAsync(
                replaceContext,
                animationInfoId,
                existingMappings,
                replacementMappings,
                cancellationToken);

            await replaceContext.FileMappings
                .Where(mapping => mapping.AnimationInfoId == animationInfoId)
                .ExecuteDeleteAsync(cancellationToken);
            if (replacementMappings.Count > 0)
                await replaceContext.FileMappings.AddRangeAsync(
                    replacementMappings,
                    cancellationToken);
            current.StateVersion = checked(current.StateVersion + 1);
            await replaceContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<IReadOnlyList<FileMapping>> GetForAnimationInfoAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        var entities = await context.FileMappings
            .AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == animationInfoId)
            .OrderBy(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<FileMapping>> GetForAnimationInfosAsync(
        IReadOnlyCollection<Guid> animationInfoIds,
        CancellationToken cancellationToken)
    {
        if (animationInfoIds.Count == 0) return [];

        var ids = animationInfoIds.Distinct().ToArray();
        var entities = await context.FileMappings
            .AsNoTracking()
            .Where(mapping => ids.Contains(mapping.AnimationInfoId))
            .OrderBy(mapping => mapping.AnimationInfoId)
            .ThenBy(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);
        return entities.Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken)
    {
        var entity = await context.FileMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VirtualPath == virtualPath, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<FileSystemEntry?> FindFileSystemEntryAsync(
        string virtualPath,
        CancellationToken cancellationToken)
    {
        return await ProjectFileSystemEntries(context.FileSystemEntries
                .AsNoTracking()
                .Where(entry => entry.Path == virtualPath))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FileSystemEntry?> FindFileSystemEntryByIdAsync(
        Guid entryId,
        CancellationToken cancellationToken)
    {
        return await ProjectFileSystemEntries(context.FileSystemEntries
                .AsNoTracking()
                .Where(entry => entry.EntryId == entryId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FileSystemDirectoryPage?> GetImmediateChildrenPageAsync(
        string parentPath,
        long? afterCookie,
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(take), "Page size must be between 1 and 512.");
        if (afterCookie is < 0)
            throw new ArgumentOutOfRangeException(nameof(afterCookie));

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var readContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await readContext.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);

            var generation = await readContext.FileSystemDirectoryStates
                .AsNoTracking()
                .Where(state => state.Path == parentPath)
                .Select(state => (long?)state.Generation)
                .SingleOrDefaultAsync(cancellationToken);
            if (!generation.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var cursorIsValid = !afterCookie.HasValue
                                || await readContext.FileSystemEntries
                                    .AsNoTracking()
                                    .AnyAsync(
                                        entry => entry.ParentPath == parentPath
                                                 && entry.Cookie == afterCookie.Value,
                                        cancellationToken);
            if (!cursorIsValid)
            {
                await transaction.CommitAsync(cancellationToken);
                return new FileSystemDirectoryPage([], generation.Value, null, false);
            }

            var page = await ProjectFileSystemEntries(readContext.FileSystemEntries
                    .AsNoTracking()
                    .Where(entry => entry.ParentPath == parentPath
                                    && (!afterCookie.HasValue || entry.Cookie > afterCookie.Value))
                    .OrderBy(entry => entry.Cookie)
                    .Take(take + 1))
                .ToListAsync(cancellationToken);
            var hasMore = page.Count > take;
            if (hasMore) page.RemoveAt(page.Count - 1);
            await transaction.CommitAsync(cancellationToken);
            return new FileSystemDirectoryPage(
                page,
                generation.Value,
                hasMore ? page[^1].Cookie : null,
                true);
        });
    }

    public async Task<IReadOnlyList<FileSystemEntry>> GetImmediateChildrenAsync(
        string parentPath,
        CancellationToken cancellationToken)
    {
        const int pageSize = 256;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var entries = new List<FileSystemEntry>();
            long? cookie = null;
            long? generation = null;
            var restart = false;
            do
            {
                var page = await GetImmediateChildrenPageAsync(
                    parentPath,
                    cookie,
                    pageSize,
                    cancellationToken);
                if (page is null) return [];
                if (!page.CursorIsValid
                    || (generation.HasValue && generation.Value != page.Generation))
                {
                    restart = true;
                    break;
                }

                generation ??= page.Generation;
                entries.AddRange(page.Items);
                cookie = page.NextCookie;
            } while (cookie.HasValue);

            if (!restart)
            {
                return entries
                    .OrderByDescending(entry => entry.IsDirectory)
                    .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                    .ToList();
            }
        }

        throw new InvalidOperationException("The directory changed continuously while it was being enumerated.");
    }

    public Task<IReadOnlyList<VirtualPathNamespaceConflict>> FindNamespaceConflictsAsync(
        Guid animationInfoId,
        IReadOnlyCollection<string> proposedPaths,
        CancellationToken cancellationToken) =>
        VirtualPathNamespaceGuard.FindConflictsAsync(
            context,
            animationInfoId,
            proposedPaths,
            cancellationToken);

    public async Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken)
    {
        var pattern = EscapeLikePattern(virtualPathPrefix) + "%";
        var entities = await context.FileMappings
            .AsNoTracking()
            .Where(m => EF.Functions.Like(m.VirtualPath, pattern, "\\"))
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken)
    {
        var entries = await GetImmediateChildrenAsync("/", cancellationToken);
        return entries
            .Select(entry => new RootEntry(entry.Name, entry.IsDirectory))
            .ToList();
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    public async Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken)
    {
        return await context.FileSystemEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.Path == virtualPath, cancellationToken);
    }

    private static IQueryable<FileSystemEntry> ProjectFileSystemEntries(
        IQueryable<Models.FileSystemEntry> query) =>
        query
            .Select(entry => new FileSystemEntry(
                entry.EntryId,
                entry.Path,
                entry.ParentPath,
                entry.Name,
                entry.IsDirectory,
                entry.DescendantFileCount,
                entry.Cookie,
                entry.FileMapping == null
                    ? null
                    : new FileMapping(
                        entry.FileMapping.Id,
                        entry.FileMapping.AnimationInfoId,
                        entry.FileMapping.VirtualPath,
                        entry.FileMapping.PhysicalPath,
                        entry.FileMapping.FileStore)));

    public async Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        return await context.FileMappings
            .AsNoTracking()
            .AnyAsync(m => m.AnimationInfoId == animationInfoId, cancellationToken);
    }

    public async Task<bool> TryFinalizeDownloadCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var finalizeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await finalizeContext.Database
                .BeginTransactionAsync(cancellationToken);

            await MappingTransactionLock.AcquireAsync(finalizeContext, cancellationToken);
            var animationInfo = await MappingTransactionLock.LockAnimationInfoAsync(
                finalizeContext,
                animationInfoId,
                cancellationToken);
            if (animationInfo is null)
                return false;
            if (!animationInfo.IsDownloadTracked)
            {
                var alreadyFinalized = animationInfo.DownloadAttemptId is null
                                       && animationInfo.DownloadCancellationId
                                       == cancellationAttemptId;
                if (alreadyFinalized)
                    await transaction.CommitAsync(cancellationToken);
                return alreadyFinalized;
            }
            if (animationInfo.DownloadAttemptId != downloadAttemptId
                || animationInfo.DownloadCancellationId != cancellationAttemptId)
                return false;

            await finalizeContext.PlaybackProgresses
                .Where(progress => progress.AnimationInfoId == animationInfoId)
                .ExecuteDeleteAsync(cancellationToken);
            await finalizeContext.FileMappings
                .Where(mapping => mapping.AnimationInfoId == animationInfoId)
                .ExecuteDeleteAsync(cancellationToken);
            animationInfo.IsDownloadTracked = false;
            animationInfo.IsDownloadFinished = false;
            animationInfo.DownloadAttemptId = null;
            // Retain the completed cancellation id until the next Start so a
            // lost commit acknowledgement can be retried idempotently.
            animationInfo.DownloadCancellationId = cancellationAttemptId;
            animationInfo.AutomationDisposition = animationInfo.AutomationDisposition is
                SubscriptionAutomationDisposition.AutoDownloadQueued or
                SubscriptionAutomationDisposition.ManualDownloadQueued or
                SubscriptionAutomationDisposition.DownloadCompleted
                    ? SubscriptionAutomationDisposition.DownloadCancelled
                    : animationInfo.AutomationDisposition;
            animationInfo.StateVersion = checked(animationInfo.StateVersion + 1);
            await finalizeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var removeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await removeContext.Database
                .BeginTransactionAsync(cancellationToken);

            await MappingTransactionLock.AcquireAsync(removeContext, cancellationToken);
            var animationInfo = await MappingTransactionLock.LockAnimationInfoAsync(
                removeContext,
                animationInfoId,
                cancellationToken);
            await removeContext.FileMappings
                .Where(mapping => mapping.AnimationInfoId == animationInfoId)
                .ExecuteDeleteAsync(cancellationToken);
            if (animationInfo is not null)
            {
                animationInfo.StateVersion = checked(animationInfo.StateVersion + 1);
                await removeContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
