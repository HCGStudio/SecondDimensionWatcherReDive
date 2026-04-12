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
public class SyncFeed(
    IServiceProvider serviceProvider,
    ILogger<SyncFeed> logger,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory)
    : BackgroundService, IScheduledTask
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");
    private volatile bool _isRunning;
    private DateTimeOffset? _lastRunAt;

    public string Name => "SyncFeed";
    public string Description => "同步 RSS 订阅";
    public TimeSpan Interval => TimeSpan.FromMinutes(10);
    public bool IsEnabled => true;
    public DateTimeOffset? LastRunAt => _lastRunAt;
    public bool IsRunning => _isRunning;

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
                logger.LogWarning(e.Message);
            }
        }
    }

    private async Task ProcessFeed(IFeedService feedService, CancellationToken cancellationToken)
    {
        var requests = await feedService.Sync(cancellationToken);
        await Task.WhenAll(requests.Select(r => ProcessSingle(r, cancellationToken)));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunNowAsync(cancellationToken);
            await Task.Delay(Interval, cancellationToken);
        }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        try
        {
            var feeds = serviceProvider.GetServices<IFeedService>();
            await Task.WhenAll(feeds.Select(f => ProcessFeed(f, cancellationToken)));
            _lastRunAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _isRunning = false;
        }
    }
}