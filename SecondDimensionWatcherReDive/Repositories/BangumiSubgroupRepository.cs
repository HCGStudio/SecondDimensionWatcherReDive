using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class BangumiSubgroupRepository(Models.ApplicationContext context) : IBangumiSubgroupRepository
{
    public async Task<IList<BangumiSubgroup>> GetBySeasonBangumiIdAsync(Guid seasonBangumiId, CancellationToken cancellationToken)
    {
        var entities = await context.BangumiSubgroups
            .Where(s => s.SeasonBangumiId == seasonBangumiId)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<BangumiSubgroup?> FindBySeasonBangumiAndSubgroupIdAsync(Guid seasonBangumiId, int mikanSubgroupId, CancellationToken cancellationToken)
    {
        var entity = await context.BangumiSubgroups
            .FirstOrDefaultAsync(s =>
                s.SeasonBangumiId == seasonBangumiId && s.MikanSubgroupId == mikanSubgroupId, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task AddAsync(BangumiSubgroup subgroup, CancellationToken cancellationToken)
    {
        await context.BangumiSubgroups.AddAsync(subgroup.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(BangumiSubgroup subgroup, CancellationToken cancellationToken)
    {
        var entity = context.BangumiSubgroups.Local.FirstOrDefault(e => e.Id == subgroup.Id)
                     ?? await context.BangumiSubgroups.FindAsync([subgroup.Id], cancellationToken)
                     ?? throw new InvalidOperationException($"BangumiSubgroup {subgroup.Id} not found");
        subgroup.ApplyTo(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}
