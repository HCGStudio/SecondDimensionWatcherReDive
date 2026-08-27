using System.Xml.Serialization;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.Feed;

/// <summary>
///     Implements IFeedService interface, a service for handling animation feeds from Mikanani.
/// </summary>
public class MikananiFeedService(
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    ISubscriptionFeedReader feedReader)
    : IFeedService
{
    public async Task<ICollection<AnimationAddRequest>> SyncAsync(CancellationToken cancellationToken)
    {
        var configUrls = configuration.GetSection("MikananiFeeds").Get<string[]>() ?? [];

        // Also read feed URLs from DB
        await using var scope = scopeFactory.CreateAsyncScope();
        var feedRepository = scope.ServiceProvider.GetRequiredService<IFeedRepository>();
        var databaseFeeds = (await feedRepository.GetAllOrderedAsync(cancellationToken))
            .Where(feed => !string.IsNullOrWhiteSpace(feed.Url))
            .DistinctBy(feed => feed.Url, StringComparer.Ordinal)
            .ToArray();

        var databaseUrls = databaseFeeds
            .Select(feed => feed.Url)
            .ToHashSet(StringComparer.Ordinal);
        var configuredOnlyUrls = configUrls
            .Where(url => !string.IsNullOrWhiteSpace(url) && !databaseUrls.Contains(url))
            .Distinct(StringComparer.Ordinal);
        var sources = databaseFeeds
            .Select(feed => (feed.Url, FeedId: (Guid?)feed.Id))
            .Concat(configuredOnlyUrls.Select(url => (Url: url, FeedId: (Guid?)null)))
            .ToArray();

        if (sources.Length == 0)
            return Array.Empty<AnimationAddRequest>();

        var batches = await Task.WhenAll(sources.Select(source =>
            feedReader.ReadAsync(source.Url, source.FeedId, cancellationToken)));
        return batches.SelectMany(batch => batch).ToArray();
    }
#nullable disable
    [XmlRoot(ElementName = "guid")]
    public class SourceGuid
    {
        [XmlAttribute(AttributeName = "isPermaLink")]
        public bool IsPermaLink { get; set; }

        [XmlText] public string Text { get; set; }
    }

    [XmlRoot(ElementName = "torrent")]
    public class Torrent
    {
        [XmlElement(ElementName = "link")] public string Link { get; set; }

        [XmlElement(ElementName = "contentLength")]
        public long ContentLength { get; set; }

        [XmlElement(ElementName = "pubDate")] public DateTime PubDate { get; set; }

        [XmlAttribute(AttributeName = "xmlns")]
        public string Xmlns { get; set; }

        [XmlText] public string Text { get; set; }
    }

    [XmlRoot(ElementName = "enclosure")]
    public class Enclosure
    {
        [XmlAttribute(AttributeName = "type")] public string Type { get; set; }

        [XmlAttribute(AttributeName = "length")]
        public long Length { get; set; }

        [XmlAttribute(AttributeName = "url")] public string Url { get; set; }
    }

    [XmlRoot(ElementName = "item")]
    public class Item
    {
        [XmlElement(ElementName = "guid")] public SourceGuid Guid { get; set; }

        [XmlElement(ElementName = "link")] public string Link { get; set; }

        [XmlElement(ElementName = "title")] public string Title { get; set; }

        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        [XmlElement(ElementName = "torrent", Namespace = "https://mikanani.me/0.1/")]
        public Torrent Torrent { get; set; }

        [XmlElement(ElementName = "enclosure")]
        public Enclosure Enclosure { get; set; }
    }

    [XmlRoot(ElementName = "channel")]
    public class SourceChannel
    {
        [XmlElement(ElementName = "title")] public string Title { get; set; }

        [XmlElement(ElementName = "link")] public string Link { get; set; }

        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        [XmlElement(ElementName = "item")] public List<Item> Item { get; set; }
    }

    [XmlRoot(ElementName = "rss")]
    public class Rss
    {
        [XmlElement(ElementName = "channel")] public SourceChannel Channel { get; set; }

        [XmlAttribute(AttributeName = "version")]
        public double Version { get; set; }

        [XmlText] public string Text { get; set; }
    }
}
