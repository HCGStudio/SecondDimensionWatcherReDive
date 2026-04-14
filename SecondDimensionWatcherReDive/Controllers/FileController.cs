using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal partial class FileController(
    IAnimationInfoRepository animationInfoRepository,
    IFileStoreProvider fileStoreProvider,
    IDistributedCache distributedCache,
    IContentTypeProvider contentTypeProvider,
    ILogger<FileController> logger) : ControllerBase
{
    private static string GenerateToken(int length)
    {
        var arr = length > 128 ? new byte[length] : stackalloc byte[length];
        RandomNumberGenerator.Fill(arr);
        return Convert.ToBase64String(arr);
    }

    [HttpPost("generateLink")]
    public async Task<IActionResult> GetFileLink([FromBody] External.FileLinkResultRequest payload,
        CancellationToken cancellationToken)
    {
        LogGenerateLinkRequest(logger, payload.Id, payload.Path);

        var info = await animationInfoRepository.FindByIdAsync(payload.Id, cancellationToken);
        if (info is null || !info.IsDownloadFinished || info.FileStore is null || info.StorePath is null)
        {
            LogAnimationNotFound(logger, payload.Id);
            return NotFound();
        }

        var fileStore = fileStoreProvider.GetRequiredClient(info.FileStore);
        var storePathInfo = await fileStore.FileInfo(info.StorePath);
        LogStorePathInfo(logger, info.StorePath, storePathInfo.IsDirectory);

        string targetPath;
        if (string.IsNullOrWhiteSpace(payload.Path))
        {
            targetPath = Path.GetFullPath(info.StorePath);
            LogResolvedTargetPath(logger, targetPath, "storePath directly");
        }
        else if (storePathInfo.IsDirectory)
        {
            targetPath = Path.GetFullPath(Path.Combine(info.StorePath, payload.Path));
            LogResolvedTargetPath(logger, targetPath, "directory + relative path");
        }
        else
        {
            targetPath = Path.GetFullPath(info.StorePath);
            LogResolvedTargetPath(logger, targetPath, "storePath is file, ignoring relative path");
        }

        if (!await fileStore.Exist(targetPath))
        {
            LogTargetPathNotFound(logger, targetPath);
            return NotFound();
        }

        var token = GenerateToken(64);
        await distributedCache.SetStringAsync(token,
            JsonSerializer.Serialize(new External.FileStoreToken(targetPath, info.FileStore), External.AppJsonSerializerContext.Default.FileStoreToken),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) },
            cancellationToken);
        var url = Url.ActionLink(nameof(GetFile), values: new { token })!;
        LogLinkGenerated(logger, payload.Id, url);
        return Ok(new External.FileLinkResultResponse(url));
    }

    [AllowAnonymous]
    [HttpGet("play")]
    public async Task<IActionResult> GetFile([FromQuery] [Required] string token,
        CancellationToken cancellationToken)
    {
        var json = await distributedCache.GetStringAsync(token, cancellationToken);
        var fileStoreToken = json is null ? null : JsonSerializer.Deserialize(json, External.AppJsonSerializerContext.Default.FileStoreToken);
        if (fileStoreToken is null)
        {
            LogPlayTokenInvalid(logger);
            return NotFound();
        }

        var fileStore = fileStoreProvider.GetRequiredClient(fileStoreToken.FileStore);
        var fileInfo = await fileStore.FileInfo(fileStoreToken.Path);

        var contentType = contentTypeProvider.TryGetContentType(fileInfo.FileName, out var type)
            ? type
            : "application/octet-stream";

        LogStreamingFile(logger, fileStoreToken.Path, contentType);
        return File(await fileStore.OpenReadStream(fileStoreToken.Path), contentType, fileInfo.FileName);
    }

    [HttpGet("list")]
    public async Task<IActionResult> GetSubDir([FromQuery] [Required] Guid id,
        [FromQuery] string? relativeDir,
        CancellationToken cancellationToken)
    {
        LogListRequest(logger, id, relativeDir);

        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);
        if (info is null || !info.IsDownloadFinished || info.FileStore is null || info.StorePath is null)
        {
            LogAnimationNotFound(logger, id);
            return NotFound();
        }

        var fileStore = fileStoreProvider.GetRequiredClient(info.FileStore);
        var targetPath = Path.GetFullPath(string.IsNullOrWhiteSpace(relativeDir)
            ? info.StorePath
            : Path.Combine(info.StorePath, relativeDir));

        if (!await fileStore.Exist(targetPath))
        {
            LogTargetPathNotFound(logger, targetPath);
            return NotFound();
        }

        var fileInfo = await fileStore.FileInfo(targetPath);
        LogListPathInfo(logger, targetPath, fileInfo.IsDirectory);

        if (!fileInfo.IsDirectory)
            return Ok(fileStore.EnumerateDirectory(targetPath)
                .Select(i => new External.FileStoreListResult(i.FileName, i.IsDirectory, i.IsDirectory ? i.FileName : null)));

        return Ok(new[] { new External.FileStoreListResult(fileInfo.FileName, false, null) });
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "GenerateLink request for animation {Id}, relative path: {Path}")]
    private static partial void LogGenerateLinkRequest(ILogger logger, Guid id, string? path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Animation {Id} not found or not eligible for file access")]
    private static partial void LogAnimationNotFound(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Debug, Message = "StorePath {StorePath} isDirectory={IsDirectory}")]
    private static partial void LogStorePathInfo(ILogger logger, string storePath, bool isDirectory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved target path: {TargetPath} ({Reason})")]
    private static partial void LogResolvedTargetPath(ILogger logger, string targetPath, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Target path does not exist: {TargetPath}")]
    private static partial void LogTargetPathNotFound(ILogger logger, string targetPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated play link for animation {Id}: {Url}")]
    private static partial void LogLinkGenerated(ILogger logger, Guid id, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Play request with invalid or expired token")]
    private static partial void LogPlayTokenInvalid(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Streaming file {Path}, content-type: {ContentType}")]
    private static partial void LogStreamingFile(ILogger logger, string path, string contentType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "List request for animation {Id}, relative dir: {RelativeDir}")]
    private static partial void LogListRequest(ILogger logger, Guid id, string? relativeDir);

    [LoggerMessage(Level = LogLevel.Debug, Message = "List path {TargetPath} isDirectory={IsDirectory}")]
    private static partial void LogListPathInfo(ILogger logger, string targetPath, bool isDirectory);
}
