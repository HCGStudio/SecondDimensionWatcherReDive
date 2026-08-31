using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.WebUtilities;
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
    IDistributedCache distributedCache,
    IDeviceTokenHasher tokenHasher,
    IContentTypeProvider contentTypeProvider,
    IOptions<TokenSecurityOptions> tokenSecurityOptions,
    ILogger<FileController> logger) : ControllerBase
{
    private const string SecurePlaybackCookie = "__Host-sdw-playback";
    private const string DevelopmentPlaybackCookie = "sdw-playback";

    private static string GenerateToken(int length)
    {
        var arr = length > 128 ? new byte[length] : stackalloc byte[length];
        RandomNumberGenerator.Fill(arr);
        return WebEncoders.Base64UrlEncode(arr);
    }

    private static string ResourceCacheKey(string resourceId) =>
        "playback-resource:" + resourceId;

    private static string Fingerprint(string credential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

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

        var sessionSubject = User.FindFirst("Id")?.Value
                             ?? User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrWhiteSpace(sessionSubject))
            return Unauthorized();

        // The HMAC-derived cookie is stable for concurrent link requests and token refreshes
        // for this account. This avoids Set-Cookie races while keeping credentials out of URLs.
        var playbackSession = WebEncoders.Base64UrlEncode(SHA256.HashData(
            Encoding.UTF8.GetBytes(tokenHasher.Hash("playback-session:" + sessionSubject))));
        var resourceId = GenerateToken(16);
        var lifetime = TimeSpan.FromMinutes(tokenSecurityOptions.Value.PlaybackLinkMinutes);
        await distributedCache.SetStringAsync(ResourceCacheKey(resourceId),
            JsonSerializer.Serialize(new External.PlaybackGrant(
                    virtualPath,
                    Fingerprint(playbackSession)),
                External.AppJsonSerializerContext.Default.PlaybackGrant),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken);
        var cookieName = Request.IsHttps ? SecurePlaybackCookie : DevelopmentPlaybackCookie;
        Response.Cookies.Append(cookieName, playbackSession, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = Request.IsHttps ? "/" : "/api/file/play",
            MaxAge = lifetime,
            IsEssential = true
        });
        var url = Url.ActionLink(nameof(GetFile), values: new { resourceId })!;
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

        var json = await distributedCache.GetStringAsync(ResourceCacheKey(resourceId), cancellationToken);
        var grant = json is null
            ? null
            : JsonSerializer.Deserialize(json, External.AppJsonSerializerContext.Default.PlaybackGrant);
        var playbackSession = Request.Cookies[SecurePlaybackCookie]
                              ?? Request.Cookies[DevelopmentPlaybackCookie];
        if (grant is null || string.IsNullOrEmpty(playbackSession) ||
            !FixedTimeEquals(grant.SessionFingerprint, Fingerprint(playbackSession)))
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

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
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
