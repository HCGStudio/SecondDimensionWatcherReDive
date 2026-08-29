using System.Security.Cryptography;
using System.Text.Json;
using BencodeNET.Objects;
using BencodeNET.Parsing;
using SecondDimensionWatcherReDive.Exceptions;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     The SyncFeed class is responsible for synchronizing feeds at regular intervals.
/// </summary>
public partial class SyncFeed(
    IServiceProvider serviceProvider,
    ILogger<SyncFeed> logger,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    ISubscriptionAutomationMatcher automationMatcher,
    IIncidentReporter? incidentReporter = null,
    ISubscriptionReleaseMetadataExtractor? metadataExtractor = null,
    IReleaseScoringService? releaseScoringService = null)
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

    private readonly record struct TorrentData(byte[] CachedDownloadData, string Hash, long? PayloadSizeBytes);

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
        BDictionary info;
        try
        {
            info = parser.Parse<BDictionary>(data).Get<BDictionary>("info")
                ?? throw new InvalidTorrentDataException(request.DownloadUrl, "info dictionary is missing");
        }
        catch (InvalidTorrentDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidTorrentDataException(request.DownloadUrl, exception.Message);
        }

        var payloadSize = GetTorrentPayloadSize(info, request.DownloadUrl);
        var hash = BitConverter
            .ToString(SHA1.HashData(
                info.EncodeAsBytes()))
            .Replace("-", "")
            .ToLower();
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

            var releaseWithSize = request with { ContentLength = torrentData.PayloadSizeBytes };
            SubscriptionAutomationEvaluation? evaluation = null;
            if (policy is not null)
            {
                evaluation = automationMatcher.Evaluate(policy, releaseWithSize);
                if (!evaluation.Matched)
                    return;
            }

            var metadata = evaluation?.Metadata ?? metadataExtractor?.Extract(releaseWithSize) ??
                new SubscriptionReleaseMetadata(null, null, null, [], torrentData.PayloadSizeBytes);
            var score = releaseScoringService?.Score(metadata, policy) ?? new ReleaseScore(0, []);
            var torrentInfoHash = request.DownloadType == FileDownloadTypes.TorrentDownload
                ? torrentData.Hash
                : null;

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
                        : JsonSerializer.Serialize(evaluation.Explanations, ExplanationJsonOptions),
                    ReleaseIdentity: ReleaseIdentity.Create(
                        request.FeedId,
                        request.FeedItemGuid,
                        request.EnclosureId,
                        torrentInfoHash,
                        request.DownloadUrl),
                    FeedItemGuid: request.FeedItemGuid,
                    EnclosureId: request.EnclosureId,
                    TorrentInfoHash: torrentInfoHash,
                    ReleaseSubtitleGroup: metadata.SubtitleGroup,
                    ReleaseResolution: metadata.Resolution,
                    ReleaseCodec: metadata.Codec,
                    ReleaseLanguages: metadata.Languages,
                    ReleaseScore: score.Value,
                    ReleaseScoreReasonsJson: JsonSerializer.Serialize(score.Reasons, ExplanationJsonOptions));
            if (!await animationInfoRepository.TryAddReleaseAsync(info, cancellationToken))
                return;

            if (incidentReporter is not null)
                await incidentReporter.ResolveAsync(
                    IncidentType.FeedFailure,
                    request.DownloadUrl,
                    cancellationToken);

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
            if (incidentReporter is not null)
            {
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.FeedFailure,
                        IncidentSeverity.Error,
                        "Feed item contains invalid torrent data",
                        e.Message,
                        request.DownloadUrl),
                    cancellationToken);
            }
        }
    }

    private async Task QueueAutomaticDownloadAsync(
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
                return;
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
            }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Message}")]
    private static partial void LogSyncFeedWarning(ILogger logger, string message);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Automatic download for feed item '{Title}' failed: {Message}")]
    private static partial void LogAutomaticDownloadWarning(ILogger logger, string title, string message);
}
