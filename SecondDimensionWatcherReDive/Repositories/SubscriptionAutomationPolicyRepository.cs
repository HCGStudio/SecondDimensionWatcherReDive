using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class SubscriptionAutomationPolicyRepository(Models.ApplicationContext context)
    : ISubscriptionAutomationPolicyRepository
{
    public async Task<IReadOnlyList<SubscriptionAutomationPolicy>> GetAllOrderedAsync(
        CancellationToken cancellationToken)
    {
        var entities = await context.SubscriptionAutomationPolicies
            .AsNoTracking()
            .OrderByDescending(policy => policy.UpdatedAt)
            .ThenBy(policy => policy.FeedId)
            .ToListAsync(cancellationToken);
        return entities.Select(policy => policy.ToRecord()).ToList();
    }

    public async Task<SubscriptionAutomationPolicy?> FindByFeedIdAsync(
        Guid feedId,
        CancellationToken cancellationToken)
    {
        var entity = await context.SubscriptionAutomationPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(policy => policy.FeedId == feedId, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<SubscriptionAutomationPolicy> UpsertAsync(
        SubscriptionAutomationPolicy policy,
        CancellationToken cancellationToken)
    {
        var entity = await context.SubscriptionAutomationPolicies
            .FirstOrDefaultAsync(candidate => candidate.FeedId == policy.FeedId, cancellationToken);

        if (entity is not null)
        {
            policy.ApplyTo(entity);
            await context.SaveChangesAsync(cancellationToken);
            return entity.ToRecord();
        }

        entity = policy.ToEntity();
        await context.SubscriptionAutomationPolicies.AddAsync(entity, cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return entity.ToRecord();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request may have inserted the one allowed policy for this feed.
            context.Entry(entity).State = EntityState.Detached;
            var concurrent = await context.SubscriptionAutomationPolicies
                .FirstOrDefaultAsync(candidate => candidate.FeedId == policy.FeedId, cancellationToken);
            if (concurrent is null) throw;

            policy.ApplyTo(concurrent);
            await context.SaveChangesAsync(cancellationToken);
            return concurrent.ToRecord();
        }
    }

    public async Task<bool> DeleteByFeedIdAsync(Guid feedId, CancellationToken cancellationToken)
    {
        var entity = await context.SubscriptionAutomationPolicies
            .FirstOrDefaultAsync(policy => policy.FeedId == feedId, cancellationToken);
        if (entity is null) return false;

        context.SubscriptionAutomationPolicies.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
