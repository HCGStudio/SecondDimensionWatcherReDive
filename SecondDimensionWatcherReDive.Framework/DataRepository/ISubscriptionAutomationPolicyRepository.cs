namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface ISubscriptionAutomationPolicyRepository
{
    Task<IReadOnlyList<SubscriptionAutomationPolicy>> GetAllOrderedAsync(
        CancellationToken cancellationToken);

    Task<SubscriptionAutomationPolicy?> FindByFeedIdAsync(
        Guid feedId,
        CancellationToken cancellationToken);

    Task<SubscriptionAutomationPolicy> UpsertAsync(
        SubscriptionAutomationPolicy policy,
        CancellationToken cancellationToken);

    Task<bool> DeleteByFeedIdAsync(
        Guid feedId,
        CancellationToken cancellationToken);
}
