using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class AnimationInfoController(
    IAnimationInfoRepository animationInfoRepository,
    IDistributedCache distributedCache,
    IFileDownloadClientProvider fileDownloadClientProvider)
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

        var updated = info with { IsDownloadTracked = true, DownloadStartTime = DateTimeOffset.Now };
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

        var updated = info with { IsDownloadTracked = false, IsDownloadFinished = false };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);
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
}
