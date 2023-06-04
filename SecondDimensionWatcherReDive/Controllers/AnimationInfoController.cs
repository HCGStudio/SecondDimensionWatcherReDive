using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AnimationInfoController(
    ApplicationContext applicationContext,
    IMemoryCache memoryCache,
    IFileDownloadClientProvider fileDownloadClientProvider)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResponseData<IEnumerable<AnimationInfoDto>>>> GetAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var coreQuery = applicationContext.AnimationInfo
            .AsNoTracking()
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync();
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ProjectToType<AnimationInfoDto>()
            .ToListAsync();
        return Ok(data.ToResponseData(totalCount));
    }

    [HttpGet("downloading")]
    public async Task<ActionResult<IEnumerable<AnimationDto>>> GetDownloadingAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var data = await applicationContext.AnimationInfo
            .AsNoTracking()
            .Where(i => i.IsDownloadTracked && !i.IsDownloadFinished)
            .OrderByDescending(i => i.PublishTime)
            .Skip(skip)
            .Take(take)
            .ProjectToType<AnimationDto>()
            .ToListAsync();
        return Ok(data.ToResponseData());
    }

    [HttpGet("downloaded")]
    public async Task<ActionResult<IEnumerable<AnimationDto>>> GetDownloadedAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var data = await applicationContext.AnimationInfo
            .AsNoTracking()
            .Where(i => i.IsDownloadFinished)
            .OrderByDescending(i => i.PublishTime)
            .Skip(skip)
            .Take(take)
            .ProjectToType<AnimationDto>()
            .ToListAsync();
        return Ok(data.ToResponseData());
    }

    [HttpGet("status/{id:guid}")]
    public ActionResult<FileDownloadStatus> GetDownloadStatus([FromRoute] Guid id)
    {
        var item = memoryCache.Get<FileDownloadStatus>(id);
        return item == default ? NotFound() : Ok(item);
    }

    [HttpPost("download/{id:guid}")]
    public async Task<IActionResult> StartDownload([FromRoute] Guid id)
    {
        var info = await applicationContext.AnimationInfo.FindAsync(id);

        if (info is null)
            return NotFound();

        if (info.IsDownloadTracked)
            return Conflict();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);
        var success = await downloadClient.SubmitDownloadTask(
            id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo);

        if (!success) return BadRequest();

        info.IsDownloadTracked = true;
        await applicationContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("pause/{id:guid}")]
    public async Task<IActionResult> PauseDownload([FromRoute] Guid id)
    {
        var info = await applicationContext.AnimationInfo.FindAsync(id);

        if (info is null)
            return NotFound();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);

        try
        {
            return await downloadClient.PauseDownloadTask(id, info.DownloadUrl, info.CachedDownloadData,
                info.AdditionalDownloadInfo)
                ? Ok()
                : StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (NotSupportedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
    }

    [HttpPost("resume/{id:guid}")]
    public async Task<IActionResult> ResumeDownload([FromRoute] Guid id)
    {
        var info = await applicationContext.AnimationInfo.FindAsync(id);

        if (info is null)
            return NotFound();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);

        try
        {
            return await downloadClient.ResumeDownloadTask(id, info.DownloadUrl, info.CachedDownloadData,
                info.AdditionalDownloadInfo)
                ? Ok()
                : StatusCode(StatusCodes.Status500InternalServerError);
        }
        catch (NotSupportedException)
        {
            return StatusCode(StatusCodes.Status501NotImplemented);
        }
    }
}