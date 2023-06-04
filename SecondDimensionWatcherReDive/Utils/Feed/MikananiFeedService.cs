using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Utils.Feed;

/// <summary>
///     Implements IFeedService interface, a service for handling animation feeds from Mikanani.
/// </summary>
public class MikananiFeedService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : IFeedService
{
    private readonly ConcurrentBag<AnimationAddRequest> _animations = [];
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");

    private TimeZoneInfo ChinaTimeZone { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")
        : TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public async Task<ICollection<AnimationAddRequest>> Sync(CancellationToken cancellationToken)
    {
        var feedUrls = configuration.GetSection("MikananiFeeds").Get<string[]>();
        if (feedUrls is null || feedUrls.Length == 0)
            return Array.Empty<AnimationAddRequest>();
        await Task.WhenAll(feedUrls.Select(url => ProcessFeed(url, cancellationToken)));

        var result = _animations.ToArray();
        _animations.Clear();

        return result;
    }

    /// <summary>
    ///     Asynchronous method to process a feed by sending GET requests and parsing the received XML response.
    /// </summary>
    /// <param name="url">The URL of the feed to process.</param>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the task to complete.</param>
    private async Task ProcessFeed(string url, CancellationToken cancellationToken)
    {
        var serializer = new XmlSerializer(typeof(Rss));
        var response = await _httpClient.GetStreamAsync(url, cancellationToken);

        try
        {
            if (serializer.Deserialize(response) is not Rss result)
                return;

            foreach (var animationAddRequest in result.Channel.Item.Select(i =>
                         new AnimationAddRequest(
                             new DateTimeOffset(i.Torrent.PubDate, ChinaTimeZone.BaseUtcOffset),
                             i.Title,
                             i.Description,
                             i.Enclosure.Url,
                             FileDownloadTypes.TorrentDownload,
                             string.Empty, null, null)))
                _animations.Add(animationAddRequest);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
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