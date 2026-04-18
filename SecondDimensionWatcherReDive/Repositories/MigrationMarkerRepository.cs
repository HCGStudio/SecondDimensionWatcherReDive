using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class MigrationMarkerRepository(Models.ApplicationContext context) : IMigrationMarkerRepository
{
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        return await context.MigrationMarkers
            .AsNoTracking()
            .AnyAsync(m => m.Key == key, cancellationToken);
    }

    public async Task SetAsync(string key, CancellationToken cancellationToken)
    {
        var exists = await context.MigrationMarkers
            .AsNoTracking()
            .AnyAsync(m => m.Key == key, cancellationToken);
        if (exists) return;

        context.MigrationMarkers.Add(new Models.MigrationMarker
        {
            Key = key,
            AppliedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
