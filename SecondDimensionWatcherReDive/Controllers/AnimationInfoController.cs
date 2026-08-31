using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
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
    IIncidentReporter? incidentReporter = null)
    : ControllerBase
{
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
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCursor<AnimationCatalogCursor>(cursor, out var decoded))
            return BadRequest(new { message = "Invalid catalog cursor." });
        var result = await animationInfoRepository.GetAnimationCatalogPageAsync(
            decoded,
            NormalizeTake(take),
            cancellationToken);
        if (result.CursorInvalidated) return Conflict();
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
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeCursor<AnimationInfoCursor>(cursor, out var decoded))
            return BadRequest(new { message = "Invalid animation cursor." });
        var result = await animationInfoRepository.GetUncategorizedPageAsync(
            decoded,
            NormalizeTake(take),
            cancellationToken);
        if (result.CursorInvalidated) return Conflict();
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
        var submissionAttempted = false;
        try
        {
            if (!await animationInfoRepository.TryStartDownloadAsync(
                    id,
                    downloadAttemptId,
                    DateTimeOffset.Now,
                    queuedDisposition: null,
                    cancellationToken))
                return Conflict();

            submissionAttempted = true;
            if (!await downloadClient.SubmitDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                cancellationToken))
            {
                await CompensateFailedStartAsync(
                    info,
                    downloadClient,
                    downloadAttemptId,
                    remoteMayHaveAccepted: false);
                return BadRequest();
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
                    submissionAttempted);
            }
            catch
            {
                // Preserve the initiating exception. A conditional cleanup can
                // be retried safely by the tracker or a later cancellation.
            }
            throw;
        }

        return Ok();
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
        cancellationToken.ThrowIfCancellationRequested();
        using (var beginCancellation = CreateDownloadSagaTokenSource())
        {
            if (!await animationInfoRepository.TryBeginCancelDownloadAsync(
                    id,
                    info.DownloadAttemptId,
                    cancellationAttemptId,
                    beginCancellation.Token))
                return Conflict();
        }

        var result = await downloadClient.CancelDownloadTaskAsync(
            id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            removeFile,
            cancellationToken);

        if (!result.IsSuccess)
        {
            // Keep the durable cancellation intent so the next request can
            // retry the idempotent remote delete and local finalize.
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        using var finalizeCancellation = CreateDownloadSagaTokenSource();
        var cancelled = await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
            id,
            info.DownloadAttemptId,
            cancellationAttemptId,
            finalizeCancellation.Token);
        if (!cancelled)
            return Conflict();
        return Ok();
    }

    private async Task CompensateFailedStartAsync(
        AnimationInfo info,
        IFileDownloadClient downloadClient,
        Guid downloadAttemptId,
        bool remoteMayHaveAccepted)
    {
        using var cleanup = CreateDownloadSagaTokenSource();
        if (remoteMayHaveAccepted)
        {
            try
            {
                var remoteCancellation = await downloadClient.CancelDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    removeFile: false,
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

        await animationInfoRepository.TryCancelDownloadAsync(
            info.Id,
            downloadAttemptId,
            terminalDisposition: null,
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
