using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.Feed;

public interface ISubscriptionAutomationSimulationService
{
    Task<SubscriptionAutomationSimulationResult> SimulateAsync(
        SubscriptionAutomationPolicy policy,
        CancellationToken cancellationToken);
}
