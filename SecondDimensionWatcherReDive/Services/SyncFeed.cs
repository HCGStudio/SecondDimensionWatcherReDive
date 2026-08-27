using System.Security.Cryptography;
using System.Text.Json;
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
    IServiceScopeFactory scopeFactory,
    ISubscriptionAutomationMatcher automationMatcher)
    : ScheduledTaskBase
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");
    private static readonly JsonSerializerOptions ExplanationJsonOptions = new(JsonSerializerDefaults.Web);

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
                SubscriptionAutomationPolicy? policy = null;
                SubscriptionAutomationEvaluation? evaluation = null;
                if (request.FeedId is { } feedId)
                {
                    var policyRepository = scope.ServiceProvider
                        .GetRequiredService<ISubscriptionAutomationPolicyRepository>();
                    policy = await policyRepository.FindByFeedIdAsync(feedId, cancellationToken);
                    if (policy is not null)
                    {
                        evaluation = automationMatcher.Evaluate(policy, request);
                        if (!evaluation.Matched)
                            return;
                    }
                }

                var torrentData = request.DownloadType switch
                {
                    FileDownloadTypes.TorrentDownload => await DownloadTorrentData(request, cancellationToken),
                    _ => new TorrentData(Array.Empty<byte>(), request.AdditionalDownloadInfo)
                };

                var info = new AnimationInfo(
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
                    AiRetryCount: 0,
                    SourceFeedId: request.FeedId,
                    ReleaseSizeBytes: evaluation?.Metadata.SizeBytes ?? request.ContentLength,
                    AutomationDisposition: policy?.Mode switch
                    {
                        SubscriptionAutomationMode.NotifyOnly => SubscriptionAutomationDisposition.Notified,
                        SubscriptionAutomationMode.ManualConfirm =>
                            SubscriptionAutomationDisposition.PendingConfirmation,
                        SubscriptionAutomationMode.AutoDownload =>
                            SubscriptionAutomationDisposition.AutoDownloadFailed,
                        _ => null
                    },
                    AutomationExplanationJson: evaluation is null
                        ? null
                        : JsonSerializer.Serialize(evaluation.Explanations, ExplanationJsonOptions));
                await animationInfoRepository.AddAsync(info, cancellationToken);

                if (policy?.Mode == SubscriptionAutomationMode.AutoDownload)
                    await QueueAutomaticDownloadAsync(
                        info,
                        animationInfoRepository,
                        scope.ServiceProvider.GetRequiredService<IFileDownloadClientProvider>(),
                        cancellationToken);
            }
            catch (InvalidTorrentDataException e)
            {
                LogSyncFeedWarning(logger, e.Message);
            }
        }
    }

    private async Task QueueAutomaticDownloadAsync(
        AnimationInfo info,
        IAnimationInfoRepository animationInfoRepository,
        IFileDownloadClientProvider downloadClientProvider,
        CancellationToken cancellationToken)
    {
        bool success;
        try
        {
            var downloadClient = downloadClientProvider.GetRequiredClient(info.DownloadType);
            success = await downloadClient.SubmitDownloadTaskAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAutomaticDownloadWarning(logger, info.Title, exception.Message);
            return;
        }
        var updated = info with
        {
            IsDownloadTracked = success,
            DownloadStartTime = success ? DateTimeOffset.UtcNow : default,
            AutomationDisposition = success
                ? SubscriptionAutomationDisposition.AutoDownloadQueued
                : SubscriptionAutomationDisposition.AutoDownloadFailed
        };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);
    }

    private async Task ProcessFeed(IFeedService feedService, CancellationToken cancellationToken)
    {
        var requests = await feedService.SyncAsync(cancellationToken);
        await Task.WhenAll(requests.Select(r => ProcessSingle(r, cancellationToken)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogSyncFeedWarning(ILogger logger, string message);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Automatic download for feed item '{Title}' failed: {Message}")]
    private static partial void LogAutomaticDownloadWarning(ILogger logger, string title, string message);
}
