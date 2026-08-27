using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class AnimationInfoController(
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
    IDistributedCache distributedCache,
    IFileDownloadClientProvider fileDownloadClientProvider,
    IFileMapper fileMapper)
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
    public async Task<IActionResult> GetGroupedAsync(CancellationToken cancellationToken)
    {
        var result = await animationInfoRepository.GetGroupedAsync(cancellationToken);
        return Ok(result.ToExternal());
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
        var success = await downloadClient.SubmitDownloadTaskAsync(
            id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            cancellationToken);

        if (!success) return BadRequest();

        var updated = info with
        {
            IsDownloadTracked = true,
            DownloadStartTime = DateTimeOffset.Now,
            AutomationDisposition = info.AutomationDisposition switch
            {
                SubscriptionAutomationDisposition.Notified or
                    SubscriptionAutomationDisposition.PendingConfirmation or
                    SubscriptionAutomationDisposition.AutoDownloadFailed or
                    SubscriptionAutomationDisposition.DownloadCancelled =>
                    SubscriptionAutomationDisposition.ManualDownloadQueued,
                _ => info.AutomationDisposition
            }
        };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);
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

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);
        var result = await downloadClient.CancelDownloadTaskAsync(
            id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            removeFile,
            cancellationToken);

        if (!result.IsSuccess)
            return StatusCode(StatusCodes.Status500InternalServerError);

        var updated = info with
        {
            IsDownloadTracked = false,
            IsDownloadFinished = false,
            AutomationDisposition = info.AutomationDisposition is
                SubscriptionAutomationDisposition.AutoDownloadQueued or
                SubscriptionAutomationDisposition.ManualDownloadQueued or
                SubscriptionAutomationDisposition.DownloadCompleted
                    ? SubscriptionAutomationDisposition.DownloadCancelled
                    : info.AutomationDisposition
        };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);
        await fileMappingRepository.RemoveByAnimationInfoAsync(id, cancellationToken);
        return Ok();
    }

    [HttpPost("{id:guid}/retry-inference")]
    public async Task<IActionResult> RetryInference([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);

        if (info is null)
            return NotFound();

        var updated = info with { IsAiProcessed = false, AiRetryCount = 0 };
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
            return await fileMapper.ReidentifyFilesWithAiAsync(id, cancellationToken)
                ? Ok()
                : UnprocessableEntity();
        }
        catch (AiFileNameInferenceUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
