using System.Security.Cryptography;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using SecondDimensionWatcherReDive.Exceptions;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     The SyncFeed class is responsible for synchronizing feeds at regular intervals.
/// </summary>
public partial class SyncFeed(
    IServiceProvider serviceProvider,
    ILogger<SyncFeed> logger,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory)
    : ScheduledTaskBase
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");

    public override string Id => "SyncFeed";
    public override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        var feeds = serviceProvider.GetServices<IFeedService>();
        await Task.WhenAll(feeds.Select(f => ProcessFeed(f, cancellationToken)));
    }

    private readonly record struct TorrentData(byte[] CachedDownloadData, string Hash);

    private async Task<TorrentData> DownloadTorrentData(
        AnimationAddRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _httpClient.GetByteArrayAsync(request.DownloadUrl, cancellationToken);
        if (data.Length == 0)
        {
            throw new InvalidTorrentDataException(request.DownloadUrl);
        }
        var parser = new BencodeParser();
        var hash = BitConverter
            .ToString(SHA1.HashData(
                parser.Parse<BDictionary>(data)["info"]
                    .EncodeAsBytes()))
            .Replace("-", "")
            .ToLower();
        return new TorrentData(data, hash);
    }

    private async Task ProcessSingle(AnimationAddRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var animationInfoRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();

        //Only process non-exist items
        if (await animationInfoRepository.FindByTitleAsync(request.Title, cancellationToken) == null)
        {
            try
            {
                var torrentData = request.DownloadType switch
                {
                    FileDownloadTypes.TorrentDownload => await DownloadTorrentData(request, cancellationToken),
                    _ => new TorrentData(Array.Empty<byte>(), string.Empty)
                };

                await animationInfoRepository.AddAsync(
                    new AnimationInfo(
                        Guid.NewGuid(),
                        request.Title,
                        request.Description,
                        request.PublishTime,
                        request.DownloadUrl,
                        request.DownloadType,
                        torrentData.CachedDownloadData,
                        torrentData.Hash,
                        IsDownloadTracked: false,
                        DownloadStartTime: default,
                        DownloadEndTime: default,
                        IsDownloadFinished: false,
                        FileStore: null,
                        StorePath: null,
                        Season: null,
                        Episode: null,
                        Group: null,
                        Animation: null,
                        IsAiProcessed: false,
                        AiRetryCount: 0),
                    cancellationToken);
            }
            catch (InvalidTorrentDataException e)
            {
                LogSyncFeedWarning(logger, e.Message);
            }
        }
    }

    private async Task ProcessFeed(IFeedService feedService, CancellationToken cancellationToken)
    {
        var requests = await feedService.SyncAsync(cancellationToken);
        await Task.WhenAll(requests.Select(r => ProcessSingle(r, cancellationToken)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogSyncFeedWarning(ILogger logger, string message);
}
