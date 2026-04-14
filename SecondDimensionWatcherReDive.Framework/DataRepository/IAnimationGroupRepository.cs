namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IAnimationGroupRepository
{
    Task<AnimationGroup?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task AddAsync(AnimationGroup group, CancellationToken cancellationToken);
}
