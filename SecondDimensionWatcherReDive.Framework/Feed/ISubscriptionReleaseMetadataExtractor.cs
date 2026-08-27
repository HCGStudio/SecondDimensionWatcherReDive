namespace SecondDimensionWatcherReDive.Framework.Feed;

public interface ISubscriptionReleaseMetadataExtractor
{
    SubscriptionReleaseMetadata Extract(AnimationAddRequest release);
}
