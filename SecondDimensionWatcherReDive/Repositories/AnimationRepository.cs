using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class AnimationRepository(Models.ApplicationContext context) : IAnimationRepository
{
    public async Task<Animation?> FindByTmdbIdAsync(string tmdbId, CancellationToken cancellationToken)
    {
        var entity = await context.Animations
            .FirstOrDefaultAsync(a => a.TmdbId == tmdbId, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task AddAsync(Animation animation, CancellationToken cancellationToken)
    {
        await context.Animations.AddAsync(animation.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
