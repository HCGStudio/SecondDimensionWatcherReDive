using System.Security.Cryptography;
using System.Text.Json;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using SecondDimensionWatcherReDive.Exceptions;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Utils.Incidents;
using SecondDimensionWatcherReDive.Utils.Http;
using SecondDimensionWatcherReDive.Utils.Feed;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     The SyncFeed class is responsible for synchronizing feeds at regular intervals.
/// </summary>
internal partial class SyncFeed(
    IServiceProvider serviceProvider,
    ILogger<SyncFeed> logger,
    ISafeOutboundHttpFetcher outboundFetcher,
    IServiceScopeFactory scopeFactory,
    ISubscriptionAutomationMatcher automationMatcher,
    IIncidentReporter? incidentReporter = null,
    INotificationPublisher? notificationPublisher = null)
    : ScheduledTaskBase
{
    private static readonly JsonSerializerOptions ExplanationJsonOptions = new(JsonSerializerDefaults.Web);

    public override string Id => "SyncFeed";
    public override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        var feeds = serviceProvider.GetServices<IFeedService>();
        await Task.WhenAll(feeds.Select(f => ProcessFeed(f, cancellationToken)));
    }

    internal readonly record struct TorrentData(byte[] CachedDownloadData, string Hash, long? PayloadSizeBytes);

    private async Task<TorrentData> DownloadTorrentData(
        AnimationAddRequest request,
        CancellationToken cancellationToken)
    {
        var data = await outboundFetcher.GetBytesAsync(
            request.DownloadUrl,
            OutboundPayloadKind.Torrent,
            cancellationToken);
        if (data.Length == 0)
        {
            throw new InvalidTorrentDataException(request.DownloadUrl);
        }
        return ParseTorrentData(data, request.DownloadUrl);
    }

    internal static TorrentData ParseTorrentData(byte[] data, string url)
    {
        var parser = new BencodeParser();
        BDictionary info;
        TorrentBencodeValidationResult validation;
        try
        {
            validation = TorrentBencodeComplexityValidator.Validate(data);
            if (!validation.HasInfoValue)
                throw new InvalidTorrentDataException(url, "info dictionary is missing");
            info = parser.Parse<BDictionary>(data).Get<BDictionary>("info")
                ?? throw new InvalidTorrentDataException(url, "info dictionary is missing");
        }
        catch (InvalidTorrentDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidTorrentDataException(url, exception.Message);
        }

        var payloadSize = GetTorrentPayloadSize(info, url);
        var hash = Convert.ToHexString(SHA1.HashData(
                data.AsSpan(validation.InfoValueOffset, validation.InfoValueLength)))
            .ToLowerInvariant();
        return new TorrentData(data, hash, payloadSize);
    }

    private static long GetTorrentPayloadSize(BDictionary info, string url)
    {
        var singleFileLength = info.Get<BNumber>("length");
        var files = info.Get<BList>("files");
        if ((singleFileLength is null) == (files is null))
            throw new InvalidTorrentDataException(url, "info must contain either length or files");

        try
        {
            if (singleFileLength is not null)
                return singleFileLength.Value >= 0
                    ? singleFileLength.Value
                    : throw new InvalidTorrentDataException(url, "payload length is negative");

            long total = 0;
            foreach (var item in files!.Value)
            {
                var length = (item as BDictionary)?.Get<BNumber>("length")?.Value;
                if (length is null || length < 0)
                    throw new InvalidTorrentDataException(url, "file length is missing or negative");
                total = checked(total + length.Value);
            }
            return total;
        }
        catch (OverflowException)
        {
            throw new InvalidTorrentDataException(url, "aggregate payload length exceeds supported limits");
        }
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
                if (request.FeedId is { } feedId)
                {
                    var policyRepository = scope.ServiceProvider
                        .GetRequiredService<ISubscriptionAutomationPolicyRepository>();
                    policy = await policyRepository.FindByFeedIdAsync(feedId, cancellationToken);
                }

                var torrentData = request.DownloadType switch
                {
                    FileDownloadTypes.TorrentDownload => await DownloadTorrentData(request, cancellationToken),
                    _ => new TorrentData(Array.Empty<byte>(), request.AdditionalDownloadInfo, request.ContentLength)
                };

                if (request.DownloadType == FileDownloadTypes.TorrentDownload &&
                    request.ContentLength is { } advertisedSize &&
                    advertisedSize != torrentData.PayloadSizeBytes)
                    throw new InvalidTorrentDataException(request.DownloadUrl, "advertised and declared payload sizes differ");

                SubscriptionAutomationEvaluation? evaluation = null;
                if (policy is not null)
                {
                    evaluation = automationMatcher.Evaluate(
                        policy,
                        request with { ContentLength = torrentData.PayloadSizeBytes });
                    if (!evaluation.Matched)
                        return;
                }

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
                    ReleaseSizeBytes: torrentData.PayloadSizeBytes,
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

                if (notificationPublisher is not null)
                {
                    if (policy?.Mode == SubscriptionAutomationMode.NotifyOnly)
                    {
                        await notificationPublisher.PublishAsync(new NotificationEvent(
                            NotificationEventType.ReleaseMatched,
                            $"release-matched:{info.Id}",
                            "Subscription release matched",
                            info.Title,
                            $"/todo?focus=automation:{info.Id}"), cancellationToken);
                    }
                    else if (policy?.Mode == SubscriptionAutomationMode.ManualConfirm)
                    {
                        await notificationPublisher.PublishAsync(new NotificationEvent(
                            NotificationEventType.DownloadPendingConfirmation,
                            $"download-pending-confirmation:{info.Id}",
                            "Download confirmation required",
                            info.Title,
                            $"/todo?focus=automation:{info.Id}"), cancellationToken);
                    }
                }

                if (incidentReporter is not null)
                    await incidentReporter.ResolveAsync(
                        IncidentType.FeedFailure,
                        CreateDownloadIncidentSourceId(request.DownloadUrl),
                        cancellationToken);

                if (policy?.Mode == SubscriptionAutomationMode.AutoDownload)
                {
                    var started = await QueueAutomaticDownloadAsync(
                        info,
                        animationInfoRepository,
                        scope.ServiceProvider.GetRequiredService<IFileDownloadClientProvider>(),
                        cancellationToken);
                    if (!started && notificationPublisher is not null)
                    {
                        await notificationPublisher.PublishAsync(new NotificationEvent(
                            NotificationEventType.DownloadFailed,
                            $"auto-download-failed:{info.Id}",
                            "Automatic download failed",
                            info.Title,
                            $"/todo?focus=automation:{info.Id}"), cancellationToken);
                    }
                }
            }
            catch (InvalidTorrentDataException e)
            {
                LogSyncFeedWarning(logger, e.Message);
                if (incidentReporter is not null)
                {
                    await incidentReporter.ReportAsync(new IncidentReport(
                            IncidentType.FeedFailure,
                            IncidentSeverity.Error,
                            "Feed item contains invalid torrent data",
                            e.Message,
                            CreateDownloadIncidentSourceId(request.DownloadUrl)),
                        cancellationToken);
                }
            }
        }
    }

    private async Task<bool> QueueAutomaticDownloadAsync(
        AnimationInfo info,
        IAnimationInfoRepository animationInfoRepository,
        IFileDownloadClientProvider downloadClientProvider,
        CancellationToken cancellationToken)
    {
        var downloadClient = downloadClientProvider.GetRequiredClient(info.DownloadType);
        var downloadAttemptId = Guid.NewGuid();
        var submissionAttempted = false;
        try
        {
            if (!await animationInfoRepository.TryStartDownloadAsync(
                    info.Id,
                    downloadAttemptId,
                    DateTimeOffset.UtcNow,
                    SubscriptionAutomationDisposition.AutoDownloadQueued,
                    cancellationToken))
            {
                LogAutomaticDownloadWarning(logger, info.Title, "download state changed");
                return false;
            }

            submissionAttempted = true;
            if (!await downloadClient.SubmitDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    cancellationToken))
            {
                await CompensateAutomaticStartAsync(
                    info,
                    animationInfoRepository,
                    downloadClient,
                    downloadAttemptId,
                    remoteMayHaveAccepted: false);
                LogAutomaticDownloadWarning(logger, info.Title, "download client rejected the task");
                return false;
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CompensateAutomaticStartAsync(
                    info,
                    animationInfoRepository,
                    downloadClient,
                    downloadAttemptId,
                    submissionAttempted);
            }
            catch
            {
                // Preserve task cancellation.
            }
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                await CompensateAutomaticStartAsync(
                    info,
                    animationInfoRepository,
                    downloadClient,
                    downloadAttemptId,
                    submissionAttempted);
            }
            catch
            {
                // Keep the original automatic-download failure in the log.
            }
            LogAutomaticDownloadWarning(logger, info.Title, exception.Message);
            return false;
        }
    }

    private static async Task CompensateAutomaticStartAsync(
        AnimationInfo info,
        IAnimationInfoRepository animationInfoRepository,
        IFileDownloadClient downloadClient,
        Guid downloadAttemptId,
        bool remoteMayHaveAccepted)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (remoteMayHaveAccepted)
        {
            try
            {
                var cancellation = await downloadClient.CancelDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    removeFile: false,
                    cleanup.Token);
                if (!cancellation.IsSuccess)
                {
                    await QueryDownloadProgressSafelyAsync(downloadClient, info, cleanup.Token);
                    return;
                }
            }
            catch
            {
                await QueryDownloadProgressSafelyAsync(downloadClient, info, cleanup.Token);
                return;
            }
        }

        await animationInfoRepository.TryCancelDownloadAsync(
            info.Id,
            downloadAttemptId,
            SubscriptionAutomationDisposition.AutoDownloadFailed,
            cleanup.Token);
    }

    private static async Task QueryDownloadProgressSafelyAsync(
        IFileDownloadClient downloadClient,
        AnimationInfo info,
        CancellationToken cancellationToken)
    {
        try
        {
            await downloadClient.SubmitQueryDownloadProgressAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                cancellationToken);
        }
        catch
        {
            // Startup recovery can rediscover the persisted attempt.
        }
    }

    private async Task ProcessFeed(IFeedService feedService, CancellationToken cancellationToken)
    {
        var sourceId = feedService.GetType().FullName ?? feedService.GetType().Name;
        try
        {
            var requests = await feedService.SyncAsync(cancellationToken);
            await Task.WhenAll(requests.Select(r => ProcessSingle(r, cancellationToken)));
            if (incidentReporter is not null)
                await incidentReporter.ResolveAsync(IncidentType.FeedFailure, sourceId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSyncFeedWarning(logger, ex.Message);
            if (incidentReporter is not null)
            {
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.FeedFailure,
                        IncidentSeverity.Error,
                        "Feed service cannot be synchronized",
                        ex.Message,
                        sourceId),
                    cancellationToken);
            }
        }
    }

    internal static string CreateDownloadIncidentSourceId(string downloadUrl)
    {
        var digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(downloadUrl));
        return $"torrent-url:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogSyncFeedWarning(ILogger logger, string message);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Automatic download for feed item '{Title}' failed: {Message}")]
    private static partial void LogAutomaticDownloadWarning(ILogger logger, string title, string message);
}
