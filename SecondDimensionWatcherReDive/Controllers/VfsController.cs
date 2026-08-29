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

        var children = await fileExplorer.EnumerateDirectoryAsync(
            new DirectoryToken(directoryPath, directoryName), cancellationToken);

        var results = new External.VfsEntry[children.Count];
        for (var i = 0; i < children.Count; i++)
        {
            results[i] = await BuildChildEntryAsync(children[i], cancellationToken);
        }

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

    private async Task<External.VfsEntry> BuildChildEntryAsync(IFileExploreToken token, CancellationToken cancellationToken)
    {
        switch (token)
        {
            case DirectoryToken d:
                return new External.VfsEntry(d.FileName, IsDirectory: true, Size: null, LastModifiedUtc: null);
            case FileToken f:
                var mapping = await fileMappingRepository.FindByVirtualPathAsync(f.Path, cancellationToken);
                if (mapping is null)
                    return new External.VfsEntry(f.FileName, IsDirectory: false, Size: null, LastModifiedUtc: null);
                var info = await TryStatAsync(mapping, cancellationToken);
                return new External.VfsEntry(f.FileName, IsDirectory: false, info?.Length, info?.LastModifiedUtc);
            default:
                throw new InvalidOperationException($"Unknown token type {token.GetType().FullName}");
        }
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

        var mapping = await fileMappingRepository.FindByVirtualPathAsync(trimmed, cancellationToken);
        if (mapping is not null)
            return new ResolvedResource(publicPath, trimmed, IsDirectory: false, mapping);

        var prefix = trimmed + "/";
        var children = await fileMappingRepository.GetByVirtualPathPrefixAsync(prefix, cancellationToken);
        return children.Count > 0
            ? new ResolvedResource(publicPath, trimmed, IsDirectory: true, null)
            : null;
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
