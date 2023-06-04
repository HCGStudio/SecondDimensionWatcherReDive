using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class FileController : ControllerBase
{
    private readonly ApplicationContext _applicationContext;
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly IFileStoreProvider _fileStoreProvider;
    private readonly IMemoryCache _memoryCache;

    public FileController(ApplicationContext applicationContext, IFileStoreProvider fileStoreProvider,
        IMemoryCache memoryCache, IContentTypeProvider contentTypeProvider)
    {
        _applicationContext = applicationContext;
        _fileStoreProvider = fileStoreProvider;
        _memoryCache = memoryCache;
        _contentTypeProvider = contentTypeProvider;
    }

    private static string GenerateToken(int length)
    {
        var arr = length > 128 ? new byte[length] : stackalloc byte[length];
        RandomNumberGenerator.Fill(arr);
        return Convert.ToBase64String(arr);
    }

    [HttpPost("generateLink")]
    public async Task<ActionResult<FileLinkResultResponse>> GetFileLink([FromBody] FileLinkResultRequest payload)
    {
        var info = await _applicationContext.AnimationInfo.FindAsync(payload.Id);
        if (info is null || !info.IsDownloadFinished || info.FileStore is null || info.StorePath is null)
            return NotFound();

        var fileStore = _fileStoreProvider.GetRequiredClient(info.FileStore);
        var targetPath = Path.GetFullPath(string.IsNullOrWhiteSpace(payload.Path)
            ? info.StorePath
            : Path.Combine(info.StorePath, payload.Path));
        if (!await fileStore.Exist(targetPath))
            return NotFound();

        var token = GenerateToken(64);
        _memoryCache.Set(token, new FileStoreToken(targetPath, info.FileStore), TimeSpan.FromDays(1));
        return Ok(new FileLinkResultResponse(HttpContext.Request.PathBase + $"file/play?token={token}"));
    }

    [AllowAnonymous]
    [HttpGet("play")]
    public async Task<IActionResult> GetFile([FromQuery] [Required] string token)
    {
        var fileStoreToken = _memoryCache.Get<FileStoreToken>(token);
        if (fileStoreToken is null) return NotFound();

        var fileStore = _fileStoreProvider.GetRequiredClient(fileStoreToken.FileStore);
        var fileInfo = await fileStore.FileInfo(fileStoreToken.Path);

        var contentType = _contentTypeProvider.TryGetContentType(fileInfo.FileName, out var type)
            ? type
            : "application/octet-stream";

        return File(await fileStore.OpenReadStream(fileStoreToken.Path), contentType, fileInfo.FileName);
    }

    [HttpGet("list")]
    public async Task<ActionResult<IEnumerable<FileStoreListResult>>> GetSubDir([FromQuery] [Required] Guid id,
        [FromQuery] string relativeDir)
    {
        var info = await _applicationContext.AnimationInfo.FindAsync(id);
        if (info is null || !info.IsDownloadFinished || info.FileStore is null || info.StorePath is null)
            return NotFound();

        var fileStore = _fileStoreProvider.GetRequiredClient(info.FileStore);
        var targetPath = Path.GetFullPath(string.IsNullOrWhiteSpace(relativeDir)
            ? info.StorePath
            : Path.Combine(info.StorePath, relativeDir));
        if (!await fileStore.Exist(targetPath))
            return NotFound();

        var fileInfo = await fileStore.FileInfo(targetPath);
        if (!fileInfo.IsDirectory)
            return Ok(fileStore.EnumerateDirectory(relativeDir)
                .Select(i => new FileStoreListResult(i.FileName, i.IsDirectory, i.IsDirectory ? i.FileName : null)));

        return Ok(new[] { new FileStoreListResult(fileInfo.FileName, false, null) });
    }

    public record FileLinkResultResponse(string Url);

    public record FileLinkResultRequest([Required] Guid Id, string Path);

    public record FileStoreToken(string Path, string FileStore);

    public record FileStoreListResult(string FileName, bool IsDirectory, string? Relative);
}