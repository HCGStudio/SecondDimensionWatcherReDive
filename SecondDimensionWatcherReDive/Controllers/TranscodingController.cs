using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SecondDimensionWatcherReDive.Services.Transcoding;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/transcoding")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class TranscodingController(IHlsTranscodingService transcodingService) : ControllerBase
{
    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(
        [FromBody] External.PrepareTranscodingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var selection = TranscodingSelection.Create(
                request.Quality,
                request.AudioLanguage,
                request.AudioTrackLabel,
                request.SubtitleLanguage,
                request.SubtitleTrackLabel);
            var status = await transcodingService.PrepareAsync(
                request.Id,
                request.Path,
                selection,
                cancellationToken);
            var response = ToResponse(status);
            return status.State == TranscodingJobState.Ready ? Ok(response) : Accepted(response);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid transcoding request", Detail = exception.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (TranscodingQueueFullException exception)
        {
            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ProblemDetails { Title = "Transcoding queue full", Detail = exception.Message });
        }
        catch (TranscodingDisabledException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails { Title = "Transcoding unavailable", Detail = exception.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetStatus(
        Guid sessionId,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        var status = await transcodingService.GetStatusAsync(sessionId, token, cancellationToken);
        return status is null ? NotFound() : Ok(ToResponse(status));
    }

    [AllowAnonymous]
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> Cancel(
        Guid sessionId,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        return await transcodingService.CancelAsync(sessionId, token, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/source")]
    public async Task<IActionResult> GetSource(
        Guid sessionId,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        var content = await transcodingService.OpenDirectAsync(sessionId, token, cancellationToken);
        if (content is null) return NotFound();
        SetContentHeaders(content, immutable: false);
        return File(content.Stream, content.ContentType, content.FileName, enableRangeProcessing: true);
    }

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/media.m3u8")]
    public async Task<IActionResult> GetPlaylist(
        Guid sessionId,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        var playlist = await transcodingService.GetPlaylistAsync(sessionId, token, cancellationToken);
        if (playlist is null) return NotFound();

        var rewritten = new List<string>();
        using var reader = new StringReader(playlist);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && line[0] != '#')
            {
                var segmentUrl = Url.ActionLink(
                    nameof(GetSegment),
                    values: new { sessionId, fileName = line, token });
                rewritten.Add(segmentUrl ?? line);
            }
            else
            {
                rewritten.Add(line);
            }
        }
        Response.Headers.CacheControl = "no-cache, no-store";
        return Content(string.Join('\n', rewritten) + "\n", "application/vnd.apple.mpegurl");
    }

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/segments/{fileName}")]
    public async Task<IActionResult> GetSegment(
        Guid sessionId,
        string fileName,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        var content = await transcodingService.OpenSegmentAsync(
            sessionId,
            token,
            fileName,
            cancellationToken);
        if (content is null) return NotFound();
        SetContentHeaders(content, immutable: true);
        return File(content.Stream, content.ContentType, enableRangeProcessing: true);
    }

    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/subtitles/{fileName}")]
    public async Task<IActionResult> GetSubtitle(
        Guid sessionId,
        string fileName,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        var content = await transcodingService.OpenSubtitleAsync(
            sessionId,
            token,
            fileName,
            cancellationToken);
        if (content is null) return NotFound();
        SetContentHeaders(content, immutable: true);
        return File(content.Stream, content.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken)
    {
        var snapshot = await transcodingService.GetMetricsAsync(cancellationToken);
        return Ok(new External.TranscodingMetricsResponse(
            snapshot.QueuedJobs,
            snapshot.ActiveJobs,
            snapshot.CompletedJobs,
            snapshot.FailedJobs,
            snapshot.CanceledJobs,
            snapshot.CacheHits,
            snapshot.CacheBytes,
            snapshot.AverageFirstSegmentSeconds,
            snapshot.AverageTranscodeSpeed,
            snapshot.FailureRate));
    }

    private External.TranscodingSessionResponse ToResponse(TranscodingSessionStatus status)
    {
        var statusUrl = Url.ActionLink(
            nameof(GetStatus),
            values: new { sessionId = status.SessionId, token = status.AccessToken })!;
        var cancelUrl = Url.ActionLink(
            nameof(Cancel),
            values: new { sessionId = status.SessionId, token = status.AccessToken })!;
        var playbackUrl = status.IsPlayable
            ? status.Strategy == TranscodingStrategy.Direct
                ? Url.ActionLink(
                    nameof(GetSource),
                    values: new { sessionId = status.SessionId, token = status.AccessToken })
                : Url.ActionLink(
                    nameof(GetPlaylist),
                    values: new { sessionId = status.SessionId, token = status.AccessToken })
            : null;
        var subtitles = status.Subtitles.Select(subtitle =>
            new External.TranscodingSubtitleResponse(
                $"__server_subtitle_{subtitle.FileName}",
                $"transcoding://subtitle/{subtitle.FileName}",
                subtitle.Language,
                subtitle.Label,
                subtitle.Format,
                Url.ActionLink(
                    nameof(GetSubtitle),
                    values: new
                    {
                        sessionId = status.SessionId,
                        fileName = subtitle.FileName,
                        token = status.AccessToken
                    })!)).ToArray();
        return new External.TranscodingSessionResponse(
            status.SessionId,
            status.State.ToString().ToLowerInvariant(),
            status.Strategy?.ToString().ToLowerInvariant(),
            status.IsPlayable,
            status.CacheHit,
            status.Progress,
            status.Speed,
            status.QueuePosition,
            status.Error,
            status.VideoCodec,
            status.AudioCodec,
            statusUrl,
            cancelUrl,
            playbackUrl,
            subtitles,
            status.UnsupportedSubtitleCount);
    }

    private void SetContentHeaders(TranscodingContent content, bool immutable)
    {
        Response.Headers.CacheControl = immutable
            ? "private, max-age=1209600, immutable"
            : "private, no-cache";
        if (content.LastModifiedUtc is { } lastModified)
            Response.Headers.LastModified = lastModified.ToUniversalTime().ToString("R");
        if (content.Length is { } length) Response.ContentLength = length;
        Response.Headers[HeaderNames.AcceptRanges] = "bytes";
    }
}
