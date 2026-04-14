using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class FeedRepository(Models.ApplicationContext context) : IFeedRepository
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
        var entity = await context.Feeds.FindAsync([feed.Id], cancellationToken);
        if (entity is not null)
        {
            context.Feeds.Remove(entity);
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
