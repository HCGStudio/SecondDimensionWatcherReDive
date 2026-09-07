using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/playback/timeline")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class MediaTimelineController(IMediaTimelineRepository repository, IAnimationInfoRepository animations,
    IFileMappingRepository mappings, IFileStoreProvider stores) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid animationInfoId, [FromQuery] string path, CancellationToken cancellationToken)
    {
        var media = await ResolveAsync(animationInfoId, path, cancellationToken);
        return media is null ? NotFound() : Ok(await repository.GetAsync(media.Version, media.SeasonKey, cancellationToken));
    }

    [HttpPut]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> Save([FromBody] External.MediaTimelineRequest request, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(request.DurationSeconds) || request.Points.Any(x => x is null
            || !double.IsFinite(x.StartSeconds) || !double.IsFinite(x.EndSeconds)
            || x.StartSeconds < 0 || x.StartSeconds >= request.DurationSeconds || x.EndSeconds > request.DurationSeconds
            || x.EndSeconds < x.StartSeconds || x.Kind is not ("opening" or "ending" or "chapter")
            || (x.Kind != "chapter" && x.EndSeconds <= x.StartSeconds)
            || string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 128)) return BadRequest();
        var segments = request.Points.Where(x => x.Enabled && x.Kind != "chapter").OrderBy(x => x.StartSeconds).ToArray();
        if (segments.GroupBy(x => x.Kind).Any(x => x.Count() > 1)
            || segments.Zip(segments.Skip(1)).Any(x => x.First.EndSeconds > x.Second.StartSeconds)) return BadRequest();
        var media = await ResolveAsync(request.AnimationInfoId, request.Path, cancellationToken);
        if (media is null) return NotFound();
        if (media.Version != request.MediaVersion) return Conflict();
        if (request.SeasonDefault && media.SeasonKey is null) return BadRequest();
        await repository.SaveAsync(media.Version, media.MappingId, media.SeasonKey, request.SeasonDefault, request.DurationSeconds,
            request.Points, cancellationToken);
        return NoContent();
    }

    [HttpPost("accept-season")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> Accept([FromBody] External.MediaTimelineAcceptRequest request, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(request.DurationSeconds)) return BadRequest();
        var media = await ResolveAsync(request.AnimationInfoId, request.Path, cancellationToken);
        if (media is null) return NotFound();
        if (media.Version != request.MediaVersion) return Conflict();
        if (media.SeasonKey is null) return BadRequest();
        try { await repository.AcceptSeasonAsync(media.Version, media.MappingId, media.SeasonKey, request.DurationSeconds, cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
        return NoContent();
    }

    [HttpDelete]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> Delete([FromQuery] Guid animationInfoId, [FromQuery] string path,
        [FromQuery] string mediaVersion, [FromQuery] bool seasonDefault, CancellationToken cancellationToken)
    {
        var media = await ResolveAsync(animationInfoId, path, cancellationToken);
        if (media is null) return NotFound();
        if (media.Version != mediaVersion) return Conflict();
        await repository.DeleteAsync(media.Version, media.SeasonKey, seasonDefault, cancellationToken);
        return NoContent();
    }

    private async Task<Media?> ResolveAsync(Guid id, string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.StartsWith('/') || path.Contains('\\')
            || path.Any(char.IsControl) || path.Split('/').Any(x => x is "" or "." or "..")) return null;
        var info = await animations.FindByIdWithAnimationAsync(id, cancellationToken);
        if (info?.IsDownloadFinished != true) return null;
        var virtualPath = PlaybackPathResolver.ResolveVirtualPath(info, path);
        if (!DevicePathScope.TryMapInternalToPublic(virtualPath, DevicePathScope.GetVirtualRoot(User), out _)) return null;
        var mapping = await mappings.FindByVirtualPathAsync(virtualPath, cancellationToken);
        if (mapping is null || mapping.AnimationInfoId != id || !MediaFileTypes.IsVideo(virtualPath)) return null;
        FileStoreInfo metadata;
        try { metadata = await stores.GetRequiredClient(mapping.FileStore).FileInfoAsync(mapping.PhysicalPath, cancellationToken); }
        catch (FileNotFoundException) { return null; }
        if (metadata.IsDirectory) return null;
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{mapping.Id}\0{mapping.AnimationInfoId}\0{mapping.VirtualPath}\0{mapping.FileStore}\0{mapping.PhysicalPath}\0{metadata.Length}\0{metadata.LastModifiedUtc:O}")));
        var seasonKey = info.Animation is not null && info.Group is not null && info.Season.HasValue
            ? $"season:{info.Animation.Id}:{info.Group.Id}:{info.Season}" : null;
        return new Media(mapping.Id, version, seasonKey);
    }
    private sealed record Media(Guid MappingId, string Version, string? SeasonKey);
}
