using System.Security.Cryptography;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.Feed;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     The SyncFeed class is responsible for synchronizing feeds at regular intervals.
/// </summary>
public class SyncFeed(
    IServiceProvider serviceProvider,
    ILogger<SyncFeed> logger,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");
    private readonly ILogger<SyncFeed> _logger = logger;

    private async Task<(byte[], string)> DownloadTorrentData(AnimationAddRequest request,
        CancellationToken cancellationToken)
    {
        var data = await _httpClient.GetByteArrayAsync(request.DownloadUrl, cancellationToken);
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
        if (await applicationContext.AnimationInfo.FirstOrDefaultAsync(i => i.Title == request.Title,
                cancellationToken) == null)
        {
            // Link Animation if request have correct TMDB id 
            var animation = request.Animation == null
                ? null
                : await applicationContext.Animations.FirstOrDefaultAsync(
                    a => a.TmdbId == request.Animation.TmdbId,
                    cancellationToken);

            var animationGroup = request.Group == null
                ? null
                : await applicationContext.AnimationGroups.FirstOrDefaultAsync(
                    g => g.Name == request.Group.Name,
                    cancellationToken);

            var (cachedDownloadData, additionalDownloadInfo) = request.DownloadType switch
            {
                FileDownloadTypes.TorrentDownload => await DownloadTorrentData(request, cancellationToken),
                _ => ([], string.Empty)
            };

            await applicationContext.AnimationInfo.AddAsync(
                new AnimationInfo
                {
                    Animation = animation,
                    Group = animationGroup,
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
    }

    private async Task ProcessFeed(IFeedService feedService, CancellationToken cancellationToken)
    {
        var requests = await feedService.Sync(cancellationToken);
        await Task.WhenAll(requests.Select(r => ProcessSingle(r, cancellationToken)));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        while (!cancellationToken.IsCancellationRequested)
        {
            var feeds = serviceProvider.GetServices<IFeedService>();
            await Task.WhenAll(feeds.Select(f => ProcessFeed(f, cancellationToken)));
            await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);
        }
    }
}