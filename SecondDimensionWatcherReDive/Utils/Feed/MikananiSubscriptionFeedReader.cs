using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Serialization;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Utils.Feed;

public sealed class MikananiSubscriptionFeedReader(IHttpClientFactory httpClientFactory)
    : ISubscriptionFeedReader
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");

    private static TimeZoneInfo ChinaTimeZone { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")
        : TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    public async Task<IReadOnlyList<AnimationAddRequest>> ReadAsync(
        string feedUrl,
        Guid? feedId,
        CancellationToken cancellationToken)
    {
        await using var response = await _httpClient.GetStreamAsync(feedUrl, cancellationToken);
        using var xmlReader = XmlReader.Create(response, new XmlReaderSettings
        {
            Async = false,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });

        var serializer = new XmlSerializer(typeof(MikananiFeedService.Rss));
        if (serializer.Deserialize(xmlReader) is not MikananiFeedService.Rss result ||
            result.Channel?.Item is not { Count: > 0 } items)
            return [];

        var releases = new List<AnimationAddRequest>(items.Count);
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
                item.Torrent.ContentLength > 0 ? item.Torrent.ContentLength : null));
        }

        return releases;
    }

    private static DateTimeOffset ToChinaOffset(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(value).ToOffset(ChinaTimeZone.GetUtcOffset(value));

        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, ChinaTimeZone.GetUtcOffset(unspecified));
    }
}
