using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.Feed;

public interface ISubscriptionAutomationMatcher
{
    SubscriptionAutomationEvaluation Evaluate(
        SubscriptionAutomationPolicy policy,
        AnimationAddRequest release);
}
