using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class AnimationInfoController(
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
    IDistributedCache distributedCache,
    IFileDownloadClientProvider fileDownloadClientProvider,
    IFileMapper fileMapper,
    IIncidentReporter? incidentReporter = null,
    INotificationPublisher? notificationPublisher = null)
    : ControllerBase
{
    private static readonly TimeSpan DownloadSubmissionLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DownloadSubmissionRemoteBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadCancellationLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DownloadCancellationRemoteBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadLeaseSafetyMargin = TimeSpan.FromSeconds(1);

    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        CancellationToken cancellationToken = default)
    {
        var (data, totalCount) = await animationInfoRepository.GetPagedAsync(skip, take, cancellationToken);
        return Ok(data.ToExternalResponseData(totalCount));
    }

    [HttpGet("grouped")]
    public async Task<IActionResult> GetGroupedAsync(
        [FromQuery] string? cursor,
        [FromQuery] int take = 24,
        [FromQuery] long? catalogRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCursor<AnimationCatalogCursor>(cursor, out var decoded))
            return BadRequest(new { message = "Invalid catalog cursor." });
        var result = await animationInfoRepository.GetAnimationCatalogPageAsync(
            decoded,
            NormalizeTake(take),
            cancellationToken);
        if (result.CursorInvalidated) return Conflict();
        if (catalogRevision is not null && result.Revision != catalogRevision.Value)
            return Conflict();
        return Ok(new External.AnimationCatalogResponse(
            result.Items.Select(item => item.ToExternal()).ToList(),
            EncodeCursor(result.NextCursor),
            result.Revision));
    }

    [HttpGet("catalog-revision")]
    public async Task<IActionResult> GetCatalogRevisionAsync(
        CancellationToken cancellationToken = default)
    {
        var revision = await animationInfoRepository.GetAnimationCatalogRevisionAsync(
            cancellationToken);
        return Ok(new External.AnimationCatalogRevisionResponse(revision));
    }

    [HttpGet("uncategorized")]
    public async Task<IActionResult> GetUncategorizedAsync(
        [FromQuery] string? cursor,
        [FromQuery] int take = 24,
        [FromQuery] long? catalogRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCursor<AnimationInfoCursor>(cursor, out var decoded))
            return BadRequest(new { message = "Invalid animation cursor." });
        var result = await animationInfoRepository.GetUncategorizedPageAsync(
            decoded,
            NormalizeTake(take),
            cancellationToken);
        if (result.CursorInvalidated) return Conflict();
        if (catalogRevision is not null && result.Revision != catalogRevision.Value)
            return Conflict();
        return Ok(new External.AnimationInfoSummaryResponse(
            result.Items.Select(item => item.ToExternal()).ToList(),
            EncodeCursor(result.NextCursor),
            result.Revision));
    }

    [HttpGet("grouped/{tmdbId}/episodes")]
    public async Task<IActionResult> GetEpisodesAsync(
        [FromRoute] string tmdbId,
        [FromQuery] string? cursor,
        [FromQuery] int take = 50,
        [FromQuery] long? catalogRevision = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCursor<AnimationInfoCursor>(cursor, out var decoded))
            return BadRequest(new { message = "Invalid episode cursor." });
        var result = await animationInfoRepository.GetAnimationEpisodesPageAsync(
            tmdbId,
            decoded,
            NormalizeTake(take),
            cancellationToken);
        if (result is null) return NotFound();
        if (result.CursorInvalidated) return Conflict();
        if (catalogRevision is not null && result.Revision != catalogRevision.Value)
            return Conflict();
        return Ok(new External.AnimationEpisodeResponse(
            result.Animation.ToExternal(),
            result.Episodes.Select(item => item.ToExternal()).ToList(),
            EncodeCursor(result.NextCursor),
            result.Revision));
    }

    private static int NormalizeTake(int take) => Math.Clamp(take, 1, 100);

    private static string? EncodeCursor<T>(T? cursor) where T : class
    {
        if (cursor is null) return null;
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryDecodeCursor<T>(string? cursor, out T? value) where T : class
    {
        value = null;
        if (string.IsNullOrEmpty(cursor)) return true;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            value = JsonSerializer.Deserialize<T>(Convert.FromBase64String(base64));
            return value is not null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    [HttpGet("downloading")]
    public async Task<IActionResult> GetDownloadingAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        CancellationToken cancellationToken = default)
    {
        var (data, totalCount) = await animationInfoRepository.GetDownloadingPagedAsync(skip, take, cancellationToken);
        return Ok(data.ToExternalListResponseData(totalCount));
    }

    [HttpGet("downloaded")]
    public async Task<IActionResult> GetDownloadedAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        CancellationToken cancellationToken = default)
    {
        var (data, totalCount) = await animationInfoRepository.GetDownloadedPagedAsync(skip, take, cancellationToken);
        return Ok(data.ToExternalListResponseData(totalCount));
    }

    [HttpGet("status/{id:guid}")]
    public async Task<IActionResult> GetDownloadStatus([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var json = await distributedCache.GetStringAsync(id.ToString(), cancellationToken);
        if (json is null) return NotFound();
        return Ok(JsonSerializer.Deserialize(json, External.AppJsonSerializerContext.Default.FileDownloadStatus));
    }

    [HttpPost("download/{id:guid}")]
    public async Task<IActionResult> StartDownload([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);

        if (info is null)
            return NotFound();

        if (info.IsDownloadTracked)
            return Conflict();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);
        var downloadAttemptId = Guid.NewGuid();
        var submissionLeaseId = Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        var submissionAttempted = false;
        try
        {
            var submissionLease = await animationInfoRepository.TryStartDownloadAsync(
                    id,
                    downloadAttemptId,
                    submissionLeaseId,
                    DownloadSubmissionLeaseDuration,
                    DateTimeOffset.Now,
                    queuedDisposition: null,
                    cancellationToken);
            if (submissionLease is null)
                return Conflict();

            var remainingRemoteBudget = DownloadSubmissionRemoteBudget -
                                        Stopwatch.GetElapsedTime(leaseRequestStartedAt);
            if (remainingRemoteBudget <= TimeSpan.Zero)
            {
                await CompensateFailedStartAsync(
                    info,
                    downloadClient,
                    downloadAttemptId,
                    submissionLeaseId,
                    remoteMayHaveAccepted: false);
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            using var submissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            submissionCancellation.CancelAfter(remainingRemoteBudget);
            submissionCancellation.Token.ThrowIfCancellationRequested();
            submissionAttempted = true;
            if (!await downloadClient.SubmitDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                submissionCancellation.Token))
            {
                await CompensateFailedStartAsync(
                    info,
                    downloadClient,
                    downloadAttemptId,
                    submissionLeaseId,
                    remoteMayHaveAccepted: false);
                await PublishDownloadFailureAsync(info, downloadAttemptId, cancellationToken);
                return BadRequest();
            }

            using var markCancellation = CreateDownloadSagaTokenSource();
            if (!await animationInfoRepository.TryMarkDownloadSubmittedAsync(
                    id,
                    downloadAttemptId,
                    submissionLeaseId,
                    markCancellation.Token))
            {
                await CompensateFailedStartAsync(
                    info,
                    downloadClient,
                    downloadAttemptId,
                    submissionLeaseId,
                    remoteMayHaveAccepted: true);
                return Conflict();
            }
        }
        catch
        {
            try
            {
                await CompensateFailedStartAsync(
                    info,
                    downloadClient,
                    downloadAttemptId,
                    submissionLeaseId,
                    submissionAttempted);
            }
            catch
            {
                // Preserve the initiating exception. A conditional cleanup can
                // be retried safely by the tracker or a later cancellation.
            }
            await PublishDownloadFailureAsync(info, downloadAttemptId, cancellationToken);
            throw;
        }

        return Ok();
    }

    private async Task PublishDownloadFailureAsync(
        Framework.DataRepository.AnimationInfo info,
        Guid downloadAttemptId,
        CancellationToken cancellationToken)
    {
        if (notificationPublisher is null)
            return;
        var current = await animationInfoRepository.FindByIdAsync(
            info.Id,
            cancellationToken);
        if (current is null || current.IsDownloadTracked || current.IsDownloadFinished)
            return;
        await notificationPublisher.PublishAsync(new NotificationEvent(
            NotificationEventType.DownloadFailed,
            $"download-failed:{info.Id}:{downloadAttemptId}",
            "Download failed to start",
            info.Title,
            info.Animation is null ? "/" : $"/anime/{info.Animation.TmdbId}"), cancellationToken);
    }

    [HttpPost("pause/{id:guid}")]
    public async Task<IActionResult> PauseDownload([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);

        if (info is null)
            return NotFound();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);

        try
        {
            return await downloadClient.PauseDownloadTaskAsync(id, info.DownloadUrl, info.CachedDownloadData,
                info.AdditionalDownloadInfo, cancellationToken)
                ? Ok()
                : StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (NotSupportedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
    }

    [HttpPost("resume/{id:guid}")]
    public async Task<IActionResult> ResumeDownload([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);

        if (info is null)
            return NotFound();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);

        try
        {
            return await downloadClient.ResumeDownloadTaskAsync(id, info.DownloadUrl, info.CachedDownloadData,
                info.AdditionalDownloadInfo, cancellationToken)
                ? Ok()
                : StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (NotSupportedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
    }

    [HttpDelete("cancel/{id:guid}")]
    public async Task<IActionResult> CancelDownload([FromRoute] Guid id, [FromQuery] bool removeFile = false,
        CancellationToken cancellationToken = default)
    {
        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);

        if (info is null)
            return NotFound();

        if (!info.IsDownloadTracked)
            return Conflict();

        // Imported media is registered in place and must never be passed to a
        // download client deletion flow, even when removeFile=true is requested.
        if (string.Equals(
                info.DownloadType,
                FileDownloadTypes.MediaLibraryImport,
                StringComparison.Ordinal))
            return Conflict(new { message = "Media library imports are read-only." });

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);
        // A persisted cancellation id means an earlier request reached the
        // remote delete but did not observe the local finalize commit. Reuse it
        // so cancellation can be resumed idempotently across requests.
        var cancellationAttemptId = info.DownloadCancellationId ?? Guid.NewGuid();
        var cancellationLeaseId = Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        DownloadCancellationLease? cancellationLease;
        cancellationToken.ThrowIfCancellationRequested();
        using (var beginCancellation = CreateDownloadSagaTokenSource())
        {
            cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
                    id,
                    info.DownloadAttemptId,
                    cancellationAttemptId,
                    cancellationLeaseId,
                    DownloadCancellationLeaseDuration,
                    removeFile,
                    requireUnfinished: false,
                    SubscriptionAutomationDisposition.DownloadCancelled,
                    beginCancellation.Token);
            if (cancellationLease is null)
                return Conflict();
        }

        var remainingRemoteBudget = DownloadCancellationRemoteBudget -
                                    Stopwatch.GetElapsedTime(leaseRequestStartedAt);
        if (remainingRemoteBudget <= TimeSpan.Zero)
            return Conflict();
        using var remoteCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        remoteCancellation.CancelAfter(remainingRemoteBudget);
        remoteCancellation.Token.ThrowIfCancellationRequested();
        var result = await downloadClient.CancelDownloadTaskAsync(
            id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            cancellationLease.RemoveFile,
            remoteCancellation.Token);

        if (!result.IsSuccess)
        {
            // Keep the durable cancellation intent so the next request can
            // retry the idempotent remote delete and local finalize.
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        using var finalizeCancellation = CreateLeaseBoundSagaTokenSource(
            leaseRequestStartedAt,
            DownloadCancellationLeaseDuration);
        if (finalizeCancellation is null)
            return Conflict();
        var cancelled = await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
            id,
            info.DownloadAttemptId,
            cancellationAttemptId,
            cancellationLease.Id,
            SubscriptionAutomationDisposition.DownloadCancelled,
            finalizeCancellation.Token);
        if (!cancelled)
            return Conflict();
        return Ok();
    }

    private async Task CompensateFailedStartAsync(
        AnimationInfo info,
        IFileDownloadClient downloadClient,
        Guid downloadAttemptId,
        Guid submissionLeaseId,
        bool remoteMayHaveAccepted)
    {
        using var cleanup = CreateDownloadSagaTokenSource();
        var cancellationAttemptId = Guid.NewGuid();
        var cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
            info.Id,
            downloadAttemptId,
            cancellationAttemptId,
            submissionLeaseId,
            DownloadCancellationLeaseDuration,
            removeFile: false,
            requireUnfinished: true,
            terminalDisposition: info.AutomationDisposition ==
                SubscriptionAutomationDisposition.AutoDownloadFailed
                    ? SubscriptionAutomationDisposition.AutoDownloadFailed
                    : null,
            cleanup.Token);
        if (cancellationLease is null)
            return;

        if (remoteMayHaveAccepted)
        {
            try
            {
                cleanup.Token.ThrowIfCancellationRequested();
                var remoteCancellation = await downloadClient.CancelDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    cancellationLease.RemoveFile,
                    cleanup.Token);
                if (!remoteCancellation.IsSuccess)
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

        cleanup.Token.ThrowIfCancellationRequested();
        await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
            info.Id,
            downloadAttemptId,
            cancellationAttemptId,
            cancellationLease.Id,
            terminalDisposition: info.AutomationDisposition ==
                SubscriptionAutomationDisposition.AutoDownloadFailed
                    ? SubscriptionAutomationDisposition.AutoDownloadFailed
                    : null,
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
            // The persisted attempt remains discoverable by startup recovery.
        }
    }

    private static CancellationTokenSource CreateDownloadSagaTokenSource() =>
        new(TimeSpan.FromSeconds(10));

    private static CancellationTokenSource? CreateLeaseBoundSagaTokenSource(
        long leaseRequestStartedAt,
        TimeSpan leaseDuration)
    {
        var remaining = leaseDuration -
                        Stopwatch.GetElapsedTime(leaseRequestStartedAt) -
                        DownloadLeaseSafetyMargin;
        if (remaining <= TimeSpan.Zero)
            return null;
        return new CancellationTokenSource(
            remaining < TimeSpan.FromSeconds(10) ? remaining : TimeSpan.FromSeconds(10));
    }

    [HttpPost("{id:guid}/retry-inference")]
    public async Task<IActionResult> RetryInference([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);

        if (info is null)
            return NotFound();

        var updated = info with
        {
            IsAiProcessed = false,
            AiRetryCount = 0,
            MetadataStatus = MetadataReviewStatus.Pending,
            MetadataConfidence = null,
            MetadataLastError = null,
            MetadataReviewedAt = null
        };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);
        return Ok();
    }

    [HttpPost("{id:guid}/reidentify-files/ai")]
    public async Task<IActionResult> ReidentifyFilesWithAi(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdWithAnimationAsync(id, cancellationToken);
        if (info is null) return NotFound();

        // Filename inference is only meaningful for a downloaded, known multi-episode release.
        if (!info.IsDownloadFinished
            || info.FileStore is null
            || info.StorePath is null
            || info.Animation is null
            || info.Season is null
            || info.Episode is not null)
            return Conflict();

        try
        {
            if (!await fileMapper.ReidentifyFilesWithAiAsync(id, cancellationToken))
                return UnprocessableEntity();
            if (incidentReporter is not null)
                await incidentReporter.ResolveAsync(
                    IncidentType.FileMappingFailure,
                    id.ToString(),
                    cancellationToken);
            return Ok();
        }
        catch (AiFileNameInferenceUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
