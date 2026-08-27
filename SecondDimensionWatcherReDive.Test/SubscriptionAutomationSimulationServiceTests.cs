using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.Feed;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SubscriptionAutomationSimulationServiceTests
{
    [TestMethod]
    public async Task SimulateAsync_EvaluatesFeedHistoryAndCountsMatches()
    {
        var feedId = Guid.NewGuid();
        var feed = new Feed(feedId, "https://example.com/rss", "Anime", DateTimeOffset.UtcNow);
        var feedRepository = new Mock<IFeedRepository>();
        feedRepository.Setup(repository => repository.FindByIdAsync(feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(feed);
        var reader = new Mock<ISubscriptionFeedReader>();
        reader.Setup(service => service.ReadAsync(feed.Url, feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Release("[Group] Anime 01 [1080p HEVC][CHS]", "https://example.com/1", feedId),
                Release("[Group] Anime 02 [720p AVC][CHS]", "https://example.com/2", feedId)
            ]);
        var matcher = new SubscriptionAutomationMatcher(new SubscriptionReleaseMetadataExtractor());
        var service = new SubscriptionAutomationSimulationService(
            feedRepository.Object,
            reader.Object,
            matcher);
        var policy = SubscriptionAutomationMatcherTests.Policy(
            resolutions: ["1080p"],
            feedId: feedId);

        var result = await service.SimulateAsync(policy, CancellationToken.None);

        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(1, result.Matched);
        Assert.HasCount(2, result.Entries);
        Assert.AreEqual("https://example.com/1", result.Entries[0].Id);
        Assert.IsTrue(result.Entries[0].Matched);
        Assert.IsFalse(result.Entries[1].Matched);
        Assert.HasCount(6, result.Entries[1].Explanations);
    }

    [TestMethod]
    public async Task SimulateAsync_MissingFeed_ThrowsKeyNotFoundException()
    {
        var feedRepository = new Mock<IFeedRepository>();
        feedRepository.Setup(repository => repository.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Feed?)null);
        var service = new SubscriptionAutomationSimulationService(
            feedRepository.Object,
            Mock.Of<ISubscriptionFeedReader>(),
            Mock.Of<ISubscriptionAutomationMatcher>());

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            service.SimulateAsync(SubscriptionAutomationMatcherTests.Policy(), CancellationToken.None));
    }

    private static AnimationAddRequest Release(string title, string url, Guid feedId)
    {
        return new AnimationAddRequest(
            DateTimeOffset.Parse("2026-08-27T12:00:00+08:00"),
            title,
            string.Empty,
            url,
            FileDownloadTypes.TorrentDownload,
            string.Empty,
            feedId,
            1_000_000_000);
    }
}
