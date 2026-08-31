using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal partial class FileController(
    IAnimationInfoRepository animationInfoRepository,
    IFileExplorer fileExplorer,
    PlaybackTicketService playbackTickets,
    IContentTypeProvider contentTypeProvider,
    IOptions<TokenSecurityOptions> tokenSecurityOptions,
    ILogger<FileController> logger) : ControllerBase
{
    [HttpPost("generateLink")]
    public async Task<IActionResult> GetFileLink([FromBody] External.FileLinkResultRequest payload,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private,no-store";
        LogGenerateLinkRequest(logger, payload.Id, payload.Path);

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(payload.Id, cancellationToken);
        if (info is null || !info.IsDownloadFinished)
        {
            LogAnimationNotFound(logger, payload.Id);
            return NotFound();
        }

        var virtualPath = ResolveVirtualPath(info, payload.Path);
        LogResolvedTargetPath(logger, virtualPath, "virtual path");

        var userId = User.FindFirst("Id")?.Value;
        var accessTokenId = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(accessTokenId))
            return Unauthorized();

        var lifetime = TimeSpan.FromMinutes(tokenSecurityOptions.Value.PlaybackLinkMinutes);
        var tickets = playbackTickets.Issue(userId, accessTokenId, virtualPath, lifetime);
        var cookieName = Request.IsHttps
            ? PlaybackTicketService.SecureCookieName
            : PlaybackTicketService.DevelopmentCookieName;
        Response.Cookies.Append(cookieName, tickets.CookieCredential, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = Request.IsHttps ? "/" : "/api/file/play",
            MaxAge = lifetime,
            IsEssential = true
        });
        var url = Url.ActionLink(nameof(GetFile), values: new { resourceId = tickets.ResourceId })!;
        LogLinkGenerated(logger, payload.Id, lifetime.TotalMinutes);
        return Ok(new External.FileLinkResultResponse(url));
    }

    [AllowAnonymous]
    [HttpGet("play/{resourceId}")]
    public async Task<IActionResult> GetFile([FromRoute, Required] string resourceId,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var playbackSession = Request.Cookies[PlaybackTicketService.SecureCookieName]
                              ?? Request.Cookies[PlaybackTicketService.DevelopmentCookieName];
        var grant = playbackTickets.Validate(resourceId, playbackSession);
        if (grant is null)
        {
            LogPlayTokenInvalid(logger);
            return NotFound();
        }

        var fileName = Path.GetFileName(grant.Path);
        var contentType = contentTypeProvider.TryGetContentType(fileName, out var type)
            ? type
            : "application/octet-stream";

        LogStreamingFile(logger, grant.Path, contentType);
        var stream = await fileExplorer.OpenReadStreamAsync(
            new FileToken(grant.Path, fileName), cancellationToken);
        return File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetSubDir([FromQuery, Required] Guid id,
        [FromQuery] string? relativeDir,
        CancellationToken cancellationToken)
    {
        LogListRequest(logger, id, relativeDir);

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(id, cancellationToken);
        if (info is null || !info.IsDownloadFinished)
        {
            LogAnimationNotFound(logger, id);
            return NotFound();
        }

        var virtualPath = ResolveVirtualPath(info, relativeDir);
        LogListPathInfo(logger, virtualPath, true);

        var tokens = await fileExplorer.EnumerateDirectoryAsync(
            new DirectoryToken(virtualPath, Path.GetFileName(virtualPath.TrimEnd('/'))),
            cancellationToken);

        var results = tokens.Select<IFileExploreToken, External.FileStoreListResult>(t => t switch
        {
            FileToken f => new External.FileStoreListResult(f.FileName, false, null),
            DirectoryToken d => new External.FileStoreListResult(d.FileName, true, d.FileName),
            _ => throw new InvalidOperationException()
        });
        return Ok(results);
    }

    private static string ResolveVirtualPath(AnimationInfo info, string? relative)
    {
        var root = GetAnimationVirtualRoot(info);
        if (string.IsNullOrWhiteSpace(relative)) return root;
        var trimmed = relative.Trim('/');
        return string.IsNullOrEmpty(trimmed) ? root : $"{root}/{trimmed}";
    }

    private static string GetAnimationVirtualRoot(AnimationInfo info)
    {
        if (info.Animation is null || info.Season is null) return "/unknown";
        var animationName = SanitizePathSegment(info.Animation.Name);
        var subGroup = SanitizePathSegment(info.Group?.Name ?? "Unknown");
        return $"/{animationName}/{subGroup}";
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "GenerateLink request for animation {Id}, relative path: {Path}")]
    private static partial void LogGenerateLinkRequest(ILogger logger, Guid id, string? path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Animation {Id} not found or not eligible for file access")]
    private static partial void LogAnimationNotFound(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved target path: {TargetPath} ({Reason})")]
    private static partial void LogResolvedTargetPath(ILogger logger, string targetPath, string reason);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Generated scoped play link for animation {Id}, valid for {LifetimeMinutes} minutes")]
    private static partial void LogLinkGenerated(ILogger logger, Guid id, double lifetimeMinutes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Play request with invalid or expired token")]
    private static partial void LogPlayTokenInvalid(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Streaming file {Path}, content-type: {ContentType}")]
    private static partial void LogStreamingFile(ILogger logger, string path, string contentType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "List request for animation {Id}, relative dir: {RelativeDir}")]
    private static partial void LogListRequest(ILogger logger, Guid id, string? relativeDir);

    [LoggerMessage(Level = LogLevel.Debug, Message = "List path {TargetPath} isDirectory={IsDirectory}")]
    private static partial void LogListPathInfo(ILogger logger, string targetPath, bool isDirectory);
}
