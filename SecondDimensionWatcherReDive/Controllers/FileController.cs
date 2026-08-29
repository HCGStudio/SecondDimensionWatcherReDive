using System.ComponentModel.DataAnnotations;
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
    IContentTypeProvider contentTypeProvider,
    IOptions<TokenSecurityOptions> tokenSecurityOptions,
    ILogger<FileController> logger) : ControllerBase
{
    private static string GenerateToken(int length)
    {
        var arr = length > 128 ? new byte[length] : stackalloc byte[length];
        RandomNumberGenerator.Fill(arr);
        return WebEncoders.Base64UrlEncode(arr);
    }

    private static string TokenCacheKey(string token) =>
        "playback-link:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [HttpPost("generateLink")]
    public async Task<IActionResult> GetFileLink([FromBody] External.FileLinkResultRequest payload,
        CancellationToken cancellationToken)
    {
        LogGenerateLinkRequest(logger, payload.Id, payload.Path);

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(payload.Id, cancellationToken);
        if (info is null || !info.IsDownloadFinished)
        {
            LogAnimationNotFound(logger, payload.Id);
            return NotFound();
        }

        var virtualPath = ResolveVirtualPath(info, payload.Path);
        LogResolvedTargetPath(logger, virtualPath, "virtual path");

        var token = GenerateToken(64);
        var lifetime = TimeSpan.FromMinutes(tokenSecurityOptions.Value.PlaybackLinkMinutes);
        await distributedCache.SetStringAsync(TokenCacheKey(token),
            JsonSerializer.Serialize(new External.FileStoreToken(virtualPath, string.Empty),
                External.AppJsonSerializerContext.Default.FileStoreToken),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken);
        var url = Url.ActionLink(nameof(GetFile), values: new { token })!;
        LogLinkGenerated(logger, payload.Id, lifetime.TotalMinutes);
        return Ok(new External.FileLinkResultResponse(url));
    }

    [AllowAnonymous]
    [HttpGet("play")]
    public async Task<IActionResult> GetFile([FromQuery] [Required] string token,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        var json = await distributedCache.GetStringAsync(TokenCacheKey(token), cancellationToken);
        var fileStoreToken = json is null ? null : JsonSerializer.Deserialize(json, External.AppJsonSerializerContext.Default.FileStoreToken);
        if (fileStoreToken is null)
        {
            LogPlayTokenInvalid(logger);
            return NotFound();
        }

        var fileName = Path.GetFileName(fileStoreToken.Path);
        var contentType = contentTypeProvider.TryGetContentType(fileName, out var type)
            ? type
            : "application/octet-stream";

        LogStreamingFile(logger, fileStoreToken.Path, contentType);
        var stream = await fileExplorer.OpenReadStreamAsync(
            new FileToken(fileStoreToken.Path, fileName), cancellationToken);
        return File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetSubDir([FromQuery] [Required] Guid id,
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
