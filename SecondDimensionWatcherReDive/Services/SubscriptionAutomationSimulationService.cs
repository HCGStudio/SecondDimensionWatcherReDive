using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;

namespace SecondDimensionWatcherReDive.Services;

public sealed class SubscriptionAutomationSimulationService(
    IFeedRepository feedRepository,
    ISubscriptionFeedReader feedReader,
    ISubscriptionAutomationMatcher matcher) : ISubscriptionAutomationSimulationService
{
    public async Task<SubscriptionAutomationSimulationResult> SimulateAsync(
        SubscriptionAutomationPolicy policy,
        CancellationToken cancellationToken)
    {
        var feed = await feedRepository.FindByIdAsync(policy.FeedId, cancellationToken) ??
                   throw new KeyNotFoundException($"Feed '{policy.FeedId}' was not found.");
        var releases = await feedReader.ReadAsync(feed.Url, feed.Id, cancellationToken);
        var entries = releases.Select(release =>
        {
            var evaluation = matcher.Evaluate(policy, release);
            return new SubscriptionAutomationSimulationEntry(
                release.DownloadUrl,
                release.Title,
                release.PublishTime,
                evaluation.Metadata.SizeBytes,
                evaluation.Matched,
                evaluation.Explanations);
        }).ToArray();

        return new SubscriptionAutomationSimulationResult(
            entries.Length,
            entries.Count(entry => entry.Matched),
            entries);
    }
}
