using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.Feed;

public sealed record ReleaseScore(int Value, IReadOnlyList<string> Reasons);

public interface IReleaseScoringService
{
    ReleaseScore Score(
        SubscriptionReleaseMetadata metadata,
        SubscriptionAutomationPolicy? policy);
}
