using Microsoft.EntityFrameworkCore;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Establishes the lock order shared by every transaction that mutates the
/// virtual-path namespace: global advisory transaction lock, then aggregate
/// rows ordered by identifier.
/// </summary>
internal static class MappingTransactionLock
{
    // Stable application-owned PostgreSQL advisory lock key ("SDWRMAP1").
    private const long MappingNamespaceLockKey = 0x534457524D415031;

    public static async Task AcquireAsync(
        Models.ApplicationContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({MappingNamespaceLockKey})",
            cancellationToken);
    }

    public static async Task<Models.AnimationInfo?> LockAnimationInfoAsync(
        Models.ApplicationContext context,
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        await LockAnimationInfoRowsAsync(context, [animationInfoId], cancellationToken);
        return await context.AnimationInfo
            .SingleOrDefaultAsync(info => info.Id == animationInfoId, cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<Guid, Models.AnimationInfo>> LockAnimationInfosAsync(
        Models.ApplicationContext context,
        IEnumerable<Guid> animationInfoIds,
        CancellationToken cancellationToken)
    {
        var orderedIds = animationInfoIds.Distinct().Order().ToArray();
        await LockAnimationInfoRowsAsync(context, orderedIds, cancellationToken);

        var entities = await context.AnimationInfo
            .Where(info => orderedIds.Contains(info.Id))
            .ToListAsync(cancellationToken);
        return entities.ToDictionary(info => info.Id);
    }

    private static async Task LockAnimationInfoRowsAsync(
        Models.ApplicationContext context,
        IEnumerable<Guid> orderedAnimationInfoIds,
        CancellationToken cancellationToken)
    {
        foreach (var animationInfoId in orderedAnimationInfoIds)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AnimationInfo\" WHERE \"Id\" = {animationInfoId} FOR UPDATE",
                cancellationToken);
        }
    }
}
