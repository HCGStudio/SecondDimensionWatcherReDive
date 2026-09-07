using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/vfs")]
[Authorize(AuthenticationSchemes = BasicAuthenticationHandler.SchemeName)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed partial class VfsController(
    IFileExplorer fileExplorer,
    IFileMappingRepository fileMappingRepository,
    IFileStoreProvider fileStoreProvider,
    IContentTypeProvider contentTypeProvider,
    ILogger<VfsController> logger) : ControllerBase
{
    [HttpGet("stat")]
    public async Task<IActionResult> Stat([FromQuery] string? path, CancellationToken cancellationToken)
    {
        if (!TryGetScopedPaths(path, out var publicPath, out var internalPath))
            return BadRequest();

        var resource = await ResolveAsync(publicPath, internalPath, cancellationToken);
        if (resource is null)
        {
            LogResourceMissing(logger, publicPath);
            return NotFound();
        }

        var entry = await BuildEntryAsync(resource, cancellationToken);
        return Ok(entry);
    }

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] string? path, CancellationToken cancellationToken)
    {
        if (!TryGetScopedPaths(path, out var publicPath, out var internalPath))
            return BadRequest();

        var resource = await ResolveAsync(publicPath, internalPath, cancellationToken);
        if (resource is null)
        {
            LogResourceMissing(logger, publicPath);
            return NotFound();
        }

        if (!resource.IsDirectory)
        {
            LogListOnFile(logger, publicPath);
            return BadRequest();
        }

        var directoryPath = EnsureTrailingSlash(resource.InternalPath);
        var directoryName = resource.PublicPath == "/"
            ? string.Empty
            : Path.GetFileName(resource.PublicPath.TrimEnd('/'));

        var children = await fileExplorer.GetDirectoryEntriesAsync(
            new DirectoryToken(directoryPath, directoryName), cancellationToken);

        var results = children.Select(child => new External.VfsEntry(
            child.FileName,
            child.IsDirectory,
            child.FileInfo?.Length,
            child.FileInfo?.LastModifiedUtc)).ToArray();

        return Ok(results);
    }

    [HttpGet("read")]
    public async Task<IActionResult> Read([FromQuery] string? path, CancellationToken cancellationToken)
    {
        if (!TryGetScopedPaths(path, out var publicPath, out var internalPath))
            return BadRequest();

        var resource = await ResolveAsync(publicPath, internalPath, cancellationToken);
        if (resource is null || resource.IsDirectory || resource.Mapping is null)
        {
            LogResourceMissing(logger, publicPath);
            return NotFound();
        }

        var mapping = resource.Mapping;
        var fileName = Path.GetFileName(resource.PublicPath);
        var contentType = contentTypeProvider.TryGetContentType(fileName, out var ct)
            ? ct
            : "application/octet-stream";

        var stream = await fileExplorer.OpenReadStreamAsync(
            new FileToken(mapping.VirtualPath, fileName), cancellationToken);
        return File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    private async Task<External.VfsEntry> BuildEntryAsync(ResolvedResource resource, CancellationToken cancellationToken)
    {
        var name = resource.PublicPath == "/"
            ? string.Empty
            : Path.GetFileName(resource.PublicPath.TrimEnd('/'));

        if (resource.IsDirectory || resource.Mapping is null)
            return new External.VfsEntry(name, IsDirectory: true, Size: null, LastModifiedUtc: null);

        var info = await TryStatAsync(resource.Mapping, cancellationToken);
        return new External.VfsEntry(name, IsDirectory: false, info?.Length, info?.LastModifiedUtc);
    }

    private async Task<FileStoreInfo?> TryStatAsync(FileMapping mapping, CancellationToken cancellationToken)
    {
        try
        {
            var store = fileStoreProvider.GetRequiredClient(mapping.FileStore);
            return await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
        }
        catch (Exception ex)
        {
            LogStatFailed(logger, ex, mapping.PhysicalPath);
            return null;
        }
    }

    private async Task<ResolvedResource?> ResolveAsync(
        string publicPath,
        string internalPath,
        CancellationToken cancellationToken)
    {
        if (internalPath == "/")
            return new ResolvedResource(publicPath, internalPath, IsDirectory: true, null);

        var trimmed = internalPath.TrimEnd('/');
        if (trimmed.Length == 0)
            return new ResolvedResource(publicPath, "/", IsDirectory: true, null);

        var entry = await fileMappingRepository.FindFileSystemEntryAsync(trimmed, cancellationToken);
        return entry is null
            ? null
            : new ResolvedResource(publicPath, trimmed, entry.IsDirectory, entry.Mapping);
    }

    private bool TryGetScopedPaths(
        string? raw,
        out string publicPath,
        out string internalPath) =>
        DevicePathScope.TryMapPublicToInternal(
            raw,
            DevicePathScope.GetVirtualRoot(User),
            out publicPath,
            out internalPath);

    private static string EnsureTrailingSlash(string path) => path.EndsWith('/') ? path : path + "/";

    private sealed record ResolvedResource(
        string PublicPath,
        string InternalPath,
        bool IsDirectory,
        FileMapping? Mapping);

    [LoggerMessage(Level = LogLevel.Debug, Message = "VFS resource not found: {VirtualPath}")]
    private static partial void LogResourceMissing(ILogger logger, string virtualPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "VFS list called on file path: {VirtualPath}")]
    private static partial void LogListOnFile(ILogger logger, string virtualPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to stat physical path {PhysicalPath}")]
    private static partial void LogStatFailed(ILogger logger, Exception ex, string physicalPath);
}
