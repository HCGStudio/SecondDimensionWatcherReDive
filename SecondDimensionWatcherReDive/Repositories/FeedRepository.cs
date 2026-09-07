using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class FeedRepository(Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> options) : IFeedRepository
{
    public async Task<IReadOnlyList<Feed>> GetAllOrderedAsync(CancellationToken cancellationToken)
    {
        var entities = await context.Feeds.AsNoTracking().OrderByDescending(f => f.CreatedAt).ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<string>> GetAllUrlsAsync(CancellationToken cancellationToken)
    {
        return await context.Feeds.Select(f => f.Url).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsByUrlAsync(string url, CancellationToken cancellationToken)
    {
        return context.Feeds.AnyAsync(f => f.Url == url, cancellationToken);
    }

    public async Task<Feed?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.Feeds.FindAsync([id], cancellationToken);
        return entity?.ToRecord();
    }

    public async Task AddAsync(Feed feed, CancellationToken cancellationToken)
    {
        await context.Feeds.AddAsync(feed.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(Feed feed, CancellationToken cancellationToken)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = new Models.ApplicationContext(options);
            await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(write, cancellationToken);
            var entity = await write.Feeds.FindAsync([feed.Id], cancellationToken);
            if (entity is null) return;
            var subscription = await write.Set<Models.MultiSourceSubscription>().Include(value => value.Sources)
                .SingleOrDefaultAsync(value => value.Sources.Any(source => source.FeedId == feed.Id), cancellationToken);
            if (subscription is not null)
            {
                if (subscription.Sources.Count == 1)
                    write.Remove(subscription);
                else
                {
                    var source = subscription.Sources.Single(value => value.FeedId == feed.Id);
                    subscription.Sources.Remove(source);
                    write.Remove(source);
                    var priority = 0;
                    foreach (var remaining in subscription.Sources.OrderBy(value => value.Priority))
                        remaining.Priority = priority++;
                    subscription.UpdatedAt = DateTimeOffset.UtcNow;
                    await MultiSourceSubscriptionRepository.ClearPendingDecisionsAsync(write, subscription.Id, cancellationToken);
                }
            }
            write.Feeds.Remove(entity);
            await write.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
