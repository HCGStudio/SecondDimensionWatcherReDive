using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class AnimationGroupRepository(Models.ApplicationContext context) : IAnimationGroupRepository
{
    public async Task<AnimationGroup?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        var entity = await context.AnimationGroups
            .FirstOrDefaultAsync(g => g.Name == name, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task AddAsync(AnimationGroup group, CancellationToken cancellationToken)
    {
        await context.AnimationGroups.AddAsync(group.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}
