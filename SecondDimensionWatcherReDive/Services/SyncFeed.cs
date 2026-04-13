using System.Security.Cryptography;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Exceptions;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Models;

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

    private async Task<(byte[], string)> DownloadTorrentData(
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
        return (data, hash);
    }

    private async Task ProcessSingle(AnimationAddRequest request, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var applicationContext = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

        //Only process non-exist items
        if (await applicationContext.AnimationInfo
                .FirstOrDefaultAsync(
                    i => i.Title == request.Title,
                    cancellationToken) == null)
        {
            try
            {
                var (cachedDownloadData, additionalDownloadInfo) = request.DownloadType switch
                {
                    FileDownloadTypes.TorrentDownload => await DownloadTorrentData(request, cancellationToken),
                    _ => (Array.Empty<byte>(), string.Empty)
                };

                await applicationContext.AnimationInfo.AddAsync(
                    new AnimationInfo
                    {
                        Title = request.Title,
                        PublishTime = request.PublishTime,
                        Description = request.Description,
                        DownloadUrl = request.DownloadUrl,
                        DownloadType = request.DownloadType,
                        CachedDownloadData = cachedDownloadData,
                        AdditionalDownloadInfo = additionalDownloadInfo
                    },
                    cancellationToken);
                await applicationContext.SaveChangesAsync(cancellationToken);
            }
            catch (InvalidTorrentDataException e)
            {
                LogSyncFeedWarning(logger, e.Message);
            }
        }
    }

    private async Task ProcessFeed(IFeedService feedService, CancellationToken cancellationToken)
    {
        var requests = await feedService.Sync(cancellationToken);
        await Task.WhenAll(requests.Select(r => ProcessSingle(r, cancellationToken)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogSyncFeedWarning(ILogger logger, string message);
}