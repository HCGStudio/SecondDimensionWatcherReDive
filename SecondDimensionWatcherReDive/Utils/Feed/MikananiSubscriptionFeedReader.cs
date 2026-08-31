using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Utils.Http;

namespace SecondDimensionWatcherReDive.Utils.Feed;

internal sealed class MikananiSubscriptionFeedReader(
    ISafeOutboundHttpFetcher outboundFetcher,
    IOptions<OutboundHttpOptions> options)
    : ISubscriptionFeedReader
{
    private const int MaximumXmlDepth = 64;

    private static TimeZoneInfo ChinaTimeZone { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")
        : TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public async Task<IReadOnlyList<AnimationAddRequest>> ReadAsync(
        string feedUrl,
        Guid? feedId,
        CancellationToken cancellationToken)
    {
        var data = await outboundFetcher.GetBytesAsync(
            feedUrl,
            OutboundPayloadKind.Feed,
            cancellationToken);
        var maximumItems = options.Value.MaxFeedItems;
        ValidateXmlComplexity(data, maximumItems);

        using var response = new MemoryStream(data, writable: false);
        using var xmlReader = CreateXmlReader(response);

        var serializer = new XmlSerializer(typeof(MikananiFeedService.Rss));
        if (serializer.Deserialize(xmlReader) is not MikananiFeedService.Rss result ||
            result.Channel?.Item is not { Count: > 0 } items)
            return [];

        var releases = new List<AnimationAddRequest>(Math.Min(items.Count, maximumItems));
        foreach (var item in items)
        {
            if (item?.Torrent is null ||
                item.Enclosure is null ||
                string.IsNullOrWhiteSpace(item.Title) ||
                string.IsNullOrWhiteSpace(item.Enclosure.Url))
                continue;

            releases.Add(new AnimationAddRequest(
                ToChinaOffset(item.Torrent.PubDate),
                item.Title,
                item.Description ?? string.Empty,
                item.Enclosure.Url,
                FileDownloadTypes.TorrentDownload,
                string.Empty,
                feedId,
                item.Torrent.ContentLength > 0 ? item.Torrent.ContentLength : null,
                item.Guid?.Text,
                item.Enclosure.Url));
        }

        return releases;
    }

    private static void ValidateXmlComplexity(byte[] data, int maximumItems)
    {
        using var response = new MemoryStream(data, writable: false);
        using var reader = CreateXmlReader(response);
        var itemCount = 0;
        while (reader.Read())
        {
            if (reader.Depth > MaximumXmlDepth)
                throw new XmlException($"Feed XML depth exceeds {MaximumXmlDepth}.");
            if (reader.NodeType == XmlNodeType.Element &&
                string.Equals(reader.LocalName, "item", StringComparison.Ordinal) &&
                ++itemCount > maximumItems)
                throw new XmlException($"Feed item count exceeds {maximumItems}.");
        }
    }

    private static XmlReader CreateXmlReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        Async = false,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    });

    private static DateTimeOffset ToChinaOffset(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(value).ToOffset(ChinaTimeZone.GetUtcOffset(value));

        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, ChinaTimeZone.GetUtcOffset(unspecified));
    }
}
