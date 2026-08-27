using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class FileMappingRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions) : IFileMappingRepository
{
    public async Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken)
    {
        if (mappings.Count == 0) return;

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

            if (proposedPaths.Length > 0
                && await replaceContext.FileMappings.AnyAsync(
                    mapping => mapping.AnimationInfoId != animationInfoId
                               && proposedPaths.Contains(mapping.VirtualPath),
                    cancellationToken))
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

    public async Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken)
    {
        var entity = await context.FileMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VirtualPath == virtualPath, cancellationToken);
        return entity?.ToRecord();
    }

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
        // Raw SQL: the chained .Select(...).Select(...).Distinct() over IndexOf/Substring
        // does not dedupe under Npgsql 10 — three mappings under the same root produced
        // three duplicate rows at the WebDAV root. split_part is unambiguous and runs
        // server-side so we don't load every mapping.
        var rows = await context.Database
            .SqlQueryRaw<RootEntryRow>(
                """
                SELECT DISTINCT
                    split_part("VirtualPath", '/', 2) AS "Name",
                    position('/' IN substring("VirtualPath" FROM 2)) > 0 AS "IsDirectory"
                FROM "FileMappings"
                WHERE length("VirtualPath") > 1 AND "VirtualPath" LIKE '/%'
                """)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new RootEntry(r.Name, r.IsDirectory)).ToList();
    }

    private sealed record RootEntryRow(string Name, bool IsDirectory);

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    public async Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken)
    {
        return await context.FileMappings
            .AsNoTracking()
            .AnyAsync(m => m.VirtualPath == virtualPath, cancellationToken);
    }

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
            await removeContext.PlaybackProgresses
                .Where(progress => progress.AnimationInfoId == animationInfoId)
                .ExecuteDeleteAsync(cancellationToken);
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
