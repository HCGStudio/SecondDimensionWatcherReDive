using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
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
internal sealed class TranscodingController(
    IHlsTranscodingService transcodingService,
    IIdentityRepository identityRepository,
    IDataProtectionProvider dataProtectionProvider) : ControllerBase
{
    private readonly IDataProtector _accessProtector = dataProtectionProvider.CreateProtector("SDW.Transcoding.Identity.v1");

    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(
        [FromBody] External.PrepareTranscodingRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetProfileId(out var profileId) ||
            !User.TryGetSessionId(out var identitySessionId)) return Unauthorized();
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
            var accessToken = _accessProtector.Protect(
                $"{userId:N}.{identitySessionId:N}.{profileId:N}.{status.SessionId:N}.{status.AccessToken}");
            var response = ToResponse(status, accessToken);
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
        var accessToken = await ValidateAccessTokenAsync(sessionId, token, cancellationToken);
        if (accessToken is null) return NotFound();
        var status = await transcodingService.GetStatusAsync(sessionId, accessToken, cancellationToken);
        return status is null ? NotFound() : Ok(ToResponse(status, token));
    }

    [AllowAnonymous]
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> Cancel(
        Guid sessionId,
        [FromQuery][Required] string token,
        CancellationToken cancellationToken)
    {
        var accessToken = await ValidateAccessTokenAsync(sessionId, token, cancellationToken);
        if (accessToken is null) return NotFound();
        return await transcodingService.CancelAsync(sessionId, accessToken, cancellationToken)
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
        var accessToken = await ValidateAccessTokenAsync(sessionId, token, cancellationToken);
        if (accessToken is null) return NotFound();
        var content = await transcodingService.OpenDirectAsync(sessionId, accessToken, cancellationToken);
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
        var accessToken = await ValidateAccessTokenAsync(sessionId, token, cancellationToken);
        if (accessToken is null) return NotFound();
        var playlist = await transcodingService.GetPlaylistAsync(sessionId, accessToken, cancellationToken);
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
        var accessToken = await ValidateAccessTokenAsync(sessionId, token, cancellationToken);
        if (accessToken is null) return NotFound();
        var content = await transcodingService.OpenSegmentAsync(
            sessionId,
            accessToken,
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
        var accessToken = await ValidateAccessTokenAsync(sessionId, token, cancellationToken);
        if (accessToken is null) return NotFound();
        var content = await transcodingService.OpenSubtitleAsync(
            sessionId,
            accessToken,
            fileName,
            cancellationToken);
        if (content is null) return NotFound();
        SetContentHeaders(content, immutable: true);
        return File(content.Stream, content.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("metrics")]
    [Authorize(Policy = AccessPolicies.Administrator)]
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

    private External.TranscodingSessionResponse ToResponse(TranscodingSessionStatus status, string accessToken)
    {
        var statusUrl = Url.ActionLink(
            nameof(GetStatus),
            values: new { sessionId = status.SessionId, token = accessToken })!;
        var cancelUrl = Url.ActionLink(
            nameof(Cancel),
            values: new { sessionId = status.SessionId, token = accessToken })!;
        var playbackUrl = status.IsPlayable
            ? status.Strategy == TranscodingStrategy.Direct
                ? Url.ActionLink(
                    nameof(GetSource),
                    values: new { sessionId = status.SessionId, token = accessToken })
                : Url.ActionLink(
                    nameof(GetPlaylist),
                    values: new { sessionId = status.SessionId, token = accessToken })
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
                        token = accessToken
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

    private async Task<string?> ValidateAccessTokenAsync(
        Guid sessionId,
        string token,
        CancellationToken cancellationToken)
    {
        string[] parts;
        try
        {
            parts = _accessProtector.Unprotect(token).Split('.', 5);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
        if (parts.Length != 5 || !Guid.TryParseExact(parts[0], "N", out var userId) ||
            !Guid.TryParseExact(parts[1], "N", out var identitySessionId) ||
            !Guid.TryParseExact(parts[2], "N", out var profileId) ||
            !Guid.TryParseExact(parts[3], "N", out var expectedSessionId) || expectedSessionId != sessionId)
            return null;
        var authenticated = await identityRepository.GetAuthenticatedSessionAsync(
            identitySessionId, DateTimeOffset.UtcNow, cancellationToken);
        return authenticated?.User.Id == userId && authenticated.Profile.Id == profileId
            ? parts[4]
            : null;
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
