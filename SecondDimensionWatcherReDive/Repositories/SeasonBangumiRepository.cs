using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class SeasonBangumiRepository(Models.ApplicationContext context) : ISeasonBangumiRepository
{
    public async Task<IReadOnlyList<SeasonBangumi>> GetAllOrderedByDayAndTitleAsync(CancellationToken cancellationToken)
    {
        var entities = await context.SeasonBangumis
            .AsNoTracking()
            .OrderBy(b => b.DayOfWeek)
            .ThenBy(b => b.Title)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<IList<SeasonBangumi>> GetAllAsync(CancellationToken cancellationToken)
    {
        var entities = await context.SeasonBangumis.ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<SeasonBangumi?> FindByMikanIdAsync(int mikanId, CancellationToken cancellationToken)
    {
        var entity = await context.SeasonBangumis
            .FirstOrDefaultAsync(b => b.MikanId == mikanId, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<DateTimeOffset?> GetLatestScrapedAtAsync(CancellationToken cancellationToken)
    {
        return await context.SeasonBangumis
            .OrderByDescending(b => b.ScrapedAt)
            .Select(b => (DateTimeOffset?)b.ScrapedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(SeasonBangumi bangumi, CancellationToken cancellationToken)
    {
        await context.SeasonBangumis.AddAsync(bangumi.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SeasonBangumi bangumi, CancellationToken cancellationToken)
    {
        var entity = context.SeasonBangumis.Local.FirstOrDefault(e => e.Id == bangumi.Id)
                     ?? await context.SeasonBangumis.FindAsync([bangumi.Id], cancellationToken)
                     ?? throw new InvalidOperationException($"SeasonBangumi {bangumi.Id} not found");
        bangumi.ApplyTo(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveRangeAsync(IEnumerable<SeasonBangumi> bangumis, CancellationToken cancellationToken)
    {
        var ids = bangumis.Select(b => b.Id).ToHashSet();
        var entities = context.SeasonBangumis.Local
            .Where(e => ids.Contains(e.Id))
            .ToList();
        if (entities.Count == 0)
        {
            entities = ids.Select(id => new Models.SeasonBangumi { Id = id }).ToList();
            foreach (var entity in entities) context.SeasonBangumis.Attach(entity);
        }

        context.SeasonBangumis.RemoveRange(entities);
        await context.SaveChangesAsync(cancellationToken);
    }
}
