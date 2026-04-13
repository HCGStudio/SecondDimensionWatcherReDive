using System.Text.Json;
using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.Animation;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AnimationInfoController(
    ApplicationContext applicationContext,
    IDistributedCache distributedCache,
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

    [HttpGet("grouped")]
    public async Task<ActionResult<AnimationGroupedResponse>> GetGroupedAsync()
    {
        var allItems = await applicationContext.AnimationInfo
            .AsNoTracking()
            .Include(i => i.Animation)
            .Include(i => i.Group)
            .OrderByDescending(i => i.PublishTime)
            .ToListAsync();

        var categorized = allItems
            .Where(i => i.Animation != null)
            .GroupBy(i => i.Animation!.Id)
            .Select(g =>
            {
                var animation = g.First().Animation!;
                var episodes = g
                    .OrderBy(i => i.Season)
                    .ThenBy(i => i.Episode)
                    .Select(i => i.Adapt<AnimationInfoDto>())
                    .ToList();
                return new AnimationWithEpisodes
                {
                    TmdbId = animation.TmdbId,
                    Name = animation.Name,
                    OriginalName = animation.OriginalName,
                    PosterPath = animation.PosterPath,
                    EpisodeCount = episodes.Count,
                    Episodes = episodes
                };
            })
            .OrderByDescending(a => a.Episodes.Max(e => e.PublishTime))
            .ToList();

        var uncategorized = allItems
            .Where(i => i.Animation == null)
            .Select(i => i.Adapt<AnimationInfoDto>())
            .ToList();

        return Ok(new AnimationGroupedResponse
        {
            Animations = categorized,
            Uncategorized = uncategorized
        });
    }

    [HttpGet("downloading")]
    public async Task<ActionResult<ResponseData<List<AnimationInfoDto>>>> GetDownloadingAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var coreQuery = applicationContext.AnimationInfo
            .AsNoTracking()
            .Where(i => i.IsDownloadTracked && !i.IsDownloadFinished)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync();
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ProjectToType<AnimationInfoDto>()
            .ToListAsync();
        return Ok(data.ToResponseData(totalCount));
    }

    [HttpGet("downloaded")]
    public async Task<ActionResult<ResponseData<List<AnimationInfoDto>>>> GetDownloadedAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10)
    {
        var coreQuery = applicationContext.AnimationInfo
            .AsNoTracking()
            .Where(i => i.IsDownloadFinished)
            .OrderByDescending(i => i.PublishTime);

        var totalCount = await coreQuery.CountAsync();
        var data = await coreQuery
            .Skip(skip)
            .Take(take)
            .ProjectToType<AnimationInfoDto>()
            .ToListAsync();
        return Ok(data.ToResponseData(totalCount));
    }

    [HttpGet("status/{id:guid}")]
    public ActionResult<FileDownloadStatus> GetDownloadStatus([FromRoute] Guid id)
    {
        var json = distributedCache.GetString(id.ToString());
        if (json is null) return NotFound();
        return Ok(JsonSerializer.Deserialize<FileDownloadStatus>(json));
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
        info.DownloadStartTime = DateTimeOffset.Now;
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

    [HttpDelete("cancel/{id:guid}")]
    public async Task<IActionResult> CancelDownload([FromRoute] Guid id, [FromQuery] bool removeFile = false)
    {
        var info = await applicationContext.AnimationInfo.FindAsync(id);

        if (info is null)
            return NotFound();

        if (!info.IsDownloadTracked)
            return Conflict();

        var downloadClient = fileDownloadClientProvider.GetRequiredClient(info.DownloadType);
        var result = await downloadClient.CancelDownloadTask(
            id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            removeFile);

        if (!result.IsSuccess)
            return StatusCode(StatusCodes.Status500InternalServerError);

        info.IsDownloadTracked = false;
        info.IsDownloadFinished = false;
        await applicationContext.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id:guid}/retry-inference")]
    public async Task<IActionResult> RetryInference([FromRoute] Guid id)
    {
        var info = await applicationContext.AnimationInfo.FindAsync(id);

        if (info is null)
            return NotFound();

        info.IsAiProcessed = false;
        info.AiRetryCount = 0;
        await applicationContext.SaveChangesAsync();
        return Ok();
    }
}