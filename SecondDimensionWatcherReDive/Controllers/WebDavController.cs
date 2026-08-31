using System.Globalization;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.WebDav;
using SecondDimensionWatcherReDive.WebDav.Http;
using SecondDimensionWatcherReDive.WebDav.Results;
using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = BasicAuthenticationHandler.SchemeName)]
internal partial class WebDavController(
    IFileExplorer fileExplorer,
    IFileMappingRepository fileMappingRepository,
    IFileStoreProvider fileStoreProvider,
    IContentTypeProvider contentTypeProvider,
    IConfiguration configuration,
    ILogger<WebDavController> logger) : ControllerBase
{
    private const string RoutePrefix = "/webdav";
    private const string RouteTemplate = "/webdav/{*path=}";
    private const string ReadOnlyAllowHeader = "OPTIONS, PROPFIND, HEAD, GET";
    private const string WriteAllowHeader = ReadOnlyAllowHeader;
    private const string Win32ReadOnlyFile = "00000021";
    private const string Win32ReadOnlyDirectory = "00000011";

    [HttpOptions(RouteTemplate)]
    public IActionResult Options()
    {
        Response.Headers["DAV"] = "1";
        Response.Headers["MS-Author-Via"] = "DAV";
        Response.Headers["Allow"] = ReadOnlyAllowHeader;
        return NoContent();
    }

    [HttpPropFind(RouteTemplate)]
    public async Task<IActionResult> PropFind(string? path, CancellationToken cancellationToken)
    {
        var virtualPath = NormalizeVirtualPath(path);
        var depth = ParseDepth(Request.Headers[WebDavConstants.Headers.Depth].ToString());

        var resource = await ResolveAsync(virtualPath, cancellationToken);
        if (resource is null)
        {
            LogResourceMissing(logger, virtualPath);
            return NotFound();
        }

        if (depth == DepthValue.Infinity && resource.IsDirectory)
        {
            // Refuse infinite-depth listings to avoid full-table scans.
            Response.Headers["DAV"] = "1";
            return StatusCode(WebDavStatusCodes.Forbidden);
        }

        var request = await TryReadPropFindRequestAsync(cancellationToken);
        var filter = BuildFilter(request);

        var multiStatus = new MultiStatus();
        multiStatus.Responses.Add(await BuildResponseAsync(resource, filter, cancellationToken));

        if (depth == DepthValue.One && resource.IsDirectory)
        {
            var children = await fileExplorer.EnumerateDirectoryAsync(
                new DirectoryToken(EnsureTrailingSlash(resource.VirtualPath), Path.GetFileName(resource.VirtualPath.TrimEnd('/'))),
                cancellationToken);

            foreach (var child in children)
            {
                var childResource = child switch
                {
                    FileToken f => new ResolvedResource(f.Path, IsDirectory: false,
                        await fileMappingRepository.FindByVirtualPathAsync(f.Path, cancellationToken)),
                    DirectoryToken d => new ResolvedResource(d.Path, IsDirectory: true, null),
                    _ => null
                };
                if (childResource is null) continue;
                multiStatus.Responses.Add(await BuildResponseAsync(childResource, filter, cancellationToken));
            }
        }

        return new MultiStatusResult(multiStatus);
    }

    [HttpGet(RouteTemplate)]
    [HttpHead(RouteTemplate)]
    public async Task<IActionResult> GetFile(string? path, CancellationToken cancellationToken)
    {
        var virtualPath = NormalizeVirtualPath(path);
        var resource = await ResolveAsync(virtualPath, cancellationToken);
        if (resource is null)
        {
            LogResourceMissing(logger, virtualPath);
            return NotFound();
        }

        if (resource.IsDirectory)
        {
            // Collections (root or synthetic sub-directories) cannot be GETted.
            Response.Headers["Allow"] = ReadOnlyAllowHeader;
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var mapping = resource.Mapping!;
        var fileName = Path.GetFileName(mapping.VirtualPath);
        var contentType = ResolveContentType(fileName);

        var stream = await fileExplorer.OpenReadStreamAsync(new FileToken(mapping.VirtualPath, fileName), cancellationToken);
        return File(stream, contentType, fileName, enableRangeProcessing: true);
    }

    [HttpPropPatch(RouteTemplate)]
    [HttpMkcol(RouteTemplate)]
    [HttpCopy(RouteTemplate)]
    [HttpMove(RouteTemplate)]
    [HttpLock(RouteTemplate)]
    [HttpUnlock(RouteTemplate)]
    [HttpPut(RouteTemplate)]
    [HttpDelete(RouteTemplate)]
    public IActionResult MethodNotAllowed()
    {
        Response.Headers["Allow"] = WriteAllowHeader;
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private async Task<DavResponse> BuildResponseAsync(ResolvedResource resource, PropFilter filter, CancellationToken cancellationToken)
    {
        var response = new DavResponse
        {
            Href = BuildHref(resource.VirtualPath, resource.IsDirectory)
        };

        var prop = new Prop
        {
            DisplayName = resource.VirtualPath == "/"
                ? string.Empty
                : Path.GetFileName(resource.VirtualPath.TrimEnd('/'))
        };

        if (resource.IsDirectory)
        {
            prop.ResourceType = new ResourceType { IsCollection = true };
            prop.GetContentType = "httpd/unix-directory";
            prop.Win32FileAttributes = Win32ReadOnlyDirectory;
            prop.Executable = "F";
            PopulateQuota(prop);
        }
        else if (resource.Mapping is { } mapping)
        {
            prop.ResourceType = new ResourceType();
            prop.GetContentType = ResolveContentType(Path.GetFileName(mapping.VirtualPath));
            prop.Win32FileAttributes = Win32ReadOnlyFile;
            prop.Executable = "F";

            try
            {
                var store = fileStoreProvider.GetRequiredClient(mapping.FileStore);
                var info = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
                if (info.Length is { } length)
                    prop.GetContentLength = length.ToString(CultureInfo.InvariantCulture);
                if (info.LastModifiedUtc is { } modified)
                {
                    var httpDate = modified.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);
                    prop.GetLastModified = httpDate;
                    prop.CreationDate = modified.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
                    prop.GetETag = $"\"{modified.UtcTicks:x}-{info.Length ?? 0:x}\"";
                    prop.Win32CreationTime = httpDate;
                    prop.Win32LastAccessTime = httpDate;
                    prop.Win32LastModifiedTime = httpDate;
                }
            }
            catch (Exception ex)
            {
                LogStatFailed(logger, ex, mapping.PhysicalPath);
            }
        }

        ApplyFilter(prop, filter);

        response.PropStats.Add(new PropStat
        {
            Prop = prop,
            Status = WebDavStatusCodes.FormatStatusLine(WebDavStatusCodes.Ok)
        });

        if (filter.NotFoundProperties.Count > 0)
        {
            // RFC 4918 §9.1: properties the server doesn't recognize go in a separate
            // 404 propstat so clients can tell "unsupported" from "supported but empty".
            var notFoundProp = new Prop { DisplayName = null };
            foreach (var ext in filter.NotFoundProperties)
                notFoundProp.Extensions.Add((XmlElement)ext.CloneNode(deep: false));

            response.PropStats.Add(new PropStat
            {
                Prop = notFoundProp,
                Status = WebDavStatusCodes.FormatStatusLine(WebDavStatusCodes.NotFound)
            });
        }

        return response;
    }

    private async Task<ResolvedResource?> ResolveAsync(string virtualPath, CancellationToken cancellationToken)
    {
        if (virtualPath == "/") return new ResolvedResource("/", IsDirectory: true, null);

        var trimmed = virtualPath.TrimEnd('/');
        if (trimmed.Length == 0) return new ResolvedResource("/", IsDirectory: true, null);

        var mapping = await fileMappingRepository.FindByVirtualPathAsync(trimmed, cancellationToken);
        if (mapping is not null) return new ResolvedResource(trimmed, IsDirectory: false, mapping);

        var prefix = trimmed + "/";
        var children = await fileMappingRepository.GetByVirtualPathPrefixAsync(prefix, cancellationToken);
        return children.Count > 0 ? new ResolvedResource(trimmed, IsDirectory: true, null) : null;
    }

    private async Task<PropFindRequest?> TryReadPropFindRequestAsync(CancellationToken cancellationToken)
    {
        // ContentLength is null for chunked bodies, so don't short-circuit on it.
        // Only an explicit 0 means "no body".
        if (Request.ContentLength == 0) return null;
        try
        {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, cancellationToken);
            if (ms.Length == 0) return null;
            ms.Position = 0;
            return WebDavXml.Deserialize<PropFindRequest>(ms);
        }
        catch (Exception ex)
        {
            LogPropFindBodyParseFailed(logger, ex);
            return null;
        }
    }

    private string ResolveContentType(string fileName) =>
        contentTypeProvider.TryGetContentType(fileName, out var ct) ? ct : "application/octet-stream";

    private static string NormalizeVirtualPath(string? routeValue)
    {
        if (string.IsNullOrEmpty(routeValue)) return "/";
        var trimmed = routeValue.Trim('/');
        return trimmed.Length == 0 ? "/" : "/" + trimmed;
    }

    private static string EnsureTrailingSlash(string path) => path.EndsWith('/') ? path : path + "/";

    private static string BuildHref(string virtualPath, bool isDirectory)
    {
        var sb = new StringBuilder(RoutePrefix);
        if (virtualPath != "/")
        {
            foreach (var segment in virtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append('/');
                sb.Append(Uri.EscapeDataString(segment));
            }
        }

        if (isDirectory && (sb.Length == 0 || sb[^1] != '/')) sb.Append('/');
        return sb.ToString();
    }

    private static DepthValue ParseDepth(string? header)
    {
        if (string.IsNullOrEmpty(header)) return DepthValue.Infinity;
        if (header.Equals(WebDavConstants.Depth.Zero, StringComparison.OrdinalIgnoreCase)) return DepthValue.Zero;
        if (header.Equals(WebDavConstants.Depth.One, StringComparison.OrdinalIgnoreCase)) return DepthValue.One;
        return DepthValue.Infinity;
    }

    private enum DepthValue { Zero, One, Infinity }

    private enum PropFilterMode { AllProp, PropName, Subset }

    [Flags]
    private enum PropertyKeys
    {
        None = 0,
        CreationDate = 1 << 0,
        DisplayName = 1 << 1,
        GetContentLength = 1 << 2,
        GetContentType = 1 << 3,
        GetETag = 1 << 4,
        GetLastModified = 1 << 5,
        ResourceType = 1 << 6,
        LockDiscovery = 1 << 7,
        SupportedLock = 1 << 8,
        QuotaAvailableBytes = 1 << 9,
        QuotaUsedBytes = 1 << 10,
        Win32CreationTime = 1 << 11,
        Win32LastAccessTime = 1 << 12,
        Win32LastModifiedTime = 1 << 13,
        Win32FileAttributes = 1 << 14,
        Executable = 1 << 15,
        All = ~0
    }

    private sealed record PropFilter(
        PropFilterMode Mode,
        PropertyKeys Keys,
        IReadOnlyList<XmlElement> NotFoundProperties);

    private static readonly PropFilter AllPropFilter =
        new(PropFilterMode.AllProp, PropertyKeys.All, Array.Empty<XmlElement>());

    private static PropFilter BuildFilter(PropFindRequest? request)
    {
        if (request is null) return AllPropFilter;
        if (request.PropName is not null)
            return new PropFilter(PropFilterMode.PropName, PropertyKeys.All, Array.Empty<XmlElement>());
        if (request.AllProp is not null || request.Prop is null) return AllPropFilter;

        var keys = PropertyKeys.None;
        var notFound = new List<XmlElement>();
        var p = request.Prop;
        if (p.CreationDate is not null) keys |= PropertyKeys.CreationDate;
        if (p.DisplayName is not null) keys |= PropertyKeys.DisplayName;
        if (p.GetContentLength is not null) keys |= PropertyKeys.GetContentLength;
        if (p.GetContentType is not null) keys |= PropertyKeys.GetContentType;
        if (p.GetETag is not null) keys |= PropertyKeys.GetETag;
        if (p.GetLastModified is not null) keys |= PropertyKeys.GetLastModified;
        if (p.ResourceType is not null) keys |= PropertyKeys.ResourceType;
        if (p.LockDiscovery is not null) keys |= PropertyKeys.LockDiscovery;
        if (p.SupportedLock is not null) keys |= PropertyKeys.SupportedLock;
        if (p.QuotaAvailableBytes is not null) keys |= PropertyKeys.QuotaAvailableBytes;
        if (p.QuotaUsedBytes is not null) keys |= PropertyKeys.QuotaUsedBytes;
        if (p.Win32CreationTime is not null) keys |= PropertyKeys.Win32CreationTime;
        if (p.Win32LastAccessTime is not null) keys |= PropertyKeys.Win32LastAccessTime;
        if (p.Win32LastModifiedTime is not null) keys |= PropertyKeys.Win32LastModifiedTime;
        if (p.Win32FileAttributes is not null) keys |= PropertyKeys.Win32FileAttributes;
        if (p.Executable is not null) keys |= PropertyKeys.Executable;
        // Clients that declare their own xmlns prefix land in Extensions; match by local name so they still get answers.
        foreach (var ext in p.Extensions)
        {
            switch (ext.LocalName)
            {
                case "quota-available-bytes": keys |= PropertyKeys.QuotaAvailableBytes; break;
                case "quota-used-bytes": keys |= PropertyKeys.QuotaUsedBytes; break;
                case "Win32CreationTime": keys |= PropertyKeys.Win32CreationTime; break;
                case "Win32LastAccessTime": keys |= PropertyKeys.Win32LastAccessTime; break;
                case "Win32LastModifiedTime": keys |= PropertyKeys.Win32LastModifiedTime; break;
                case "Win32FileAttributes": keys |= PropertyKeys.Win32FileAttributes; break;
                case "executable": keys |= PropertyKeys.Executable; break;
                default: notFound.Add(ext); break;
            }
        }
        return new PropFilter(PropFilterMode.Subset, keys, notFound);
    }

    private static void ApplyFilter(Prop prop, PropFilter filter)
    {
        if (filter.Mode == PropFilterMode.AllProp) return;

        if (filter.Mode == PropFilterMode.PropName)
        {
            // propname must advertise property *names* — emit every supported property as
            // an empty element. The DTO maps null → omitted and ""/empty-instance → empty element,
            // so we replace each value-bearing property with a placeholder.
            prop.CreationDate = string.Empty;
            prop.DisplayName = string.Empty;
            prop.GetContentLength = string.Empty;
            prop.GetContentType = string.Empty;
            prop.GetETag = string.Empty;
            prop.GetLastModified = string.Empty;
            prop.ResourceType = new ResourceType();
            prop.LockDiscovery = new LockDiscovery();
            prop.SupportedLock = new SupportedLock();
            prop.QuotaAvailableBytes = string.Empty;
            prop.QuotaUsedBytes = string.Empty;
            prop.Win32CreationTime = string.Empty;
            prop.Win32LastAccessTime = string.Empty;
            prop.Win32LastModifiedTime = string.Empty;
            prop.Win32FileAttributes = string.Empty;
            prop.Executable = string.Empty;
            prop.Extensions.Clear();
            return;
        }

        if ((filter.Keys & PropertyKeys.CreationDate) == 0) prop.CreationDate = null;
        if ((filter.Keys & PropertyKeys.DisplayName) == 0) prop.DisplayName = null;
        if ((filter.Keys & PropertyKeys.GetContentLength) == 0) prop.GetContentLength = null;
        if ((filter.Keys & PropertyKeys.GetContentType) == 0) prop.GetContentType = null;
        if ((filter.Keys & PropertyKeys.GetETag) == 0) prop.GetETag = null;
        if ((filter.Keys & PropertyKeys.GetLastModified) == 0) prop.GetLastModified = null;
        if ((filter.Keys & PropertyKeys.ResourceType) == 0) prop.ResourceType = null;
        if ((filter.Keys & PropertyKeys.LockDiscovery) == 0) prop.LockDiscovery = null;
        if ((filter.Keys & PropertyKeys.SupportedLock) == 0) prop.SupportedLock = null;
        if ((filter.Keys & PropertyKeys.QuotaAvailableBytes) == 0) prop.QuotaAvailableBytes = null;
        if ((filter.Keys & PropertyKeys.QuotaUsedBytes) == 0) prop.QuotaUsedBytes = null;
        if ((filter.Keys & PropertyKeys.Win32CreationTime) == 0) prop.Win32CreationTime = null;
        if ((filter.Keys & PropertyKeys.Win32LastAccessTime) == 0) prop.Win32LastAccessTime = null;
        if ((filter.Keys & PropertyKeys.Win32LastModifiedTime) == 0) prop.Win32LastModifiedTime = null;
        if ((filter.Keys & PropertyKeys.Win32FileAttributes) == 0) prop.Win32FileAttributes = null;
        if ((filter.Keys & PropertyKeys.Executable) == 0) prop.Executable = null;
    }

    private sealed record ResolvedResource(string VirtualPath, bool IsDirectory, FileMapping? Mapping);

    private static readonly object QuotaLock = new();
    private static (string? Root, long Total, long Available, DateTime FetchedAt) _quotaCache;

    private void PopulateQuota(Prop prop)
    {
        var root = configuration["FileStore:Local"];
        if (string.IsNullOrWhiteSpace(root)) return;

        try
        {
            long total, available;
            lock (QuotaLock)
            {
                if (_quotaCache.Root != root || DateTime.UtcNow - _quotaCache.FetchedAt > TimeSpan.FromMinutes(1))
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
                    _quotaCache = (root, drive.TotalSize, drive.AvailableFreeSpace, DateTime.UtcNow);
                }
                total = _quotaCache.Total;
                available = _quotaCache.Available;
            }

            prop.QuotaAvailableBytes = available.ToString(CultureInfo.InvariantCulture);
            prop.QuotaUsedBytes = Math.Max(0, total - available).ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            LogQuotaFailed(logger, ex, root);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "WebDAV resource not found: {VirtualPath}")]
    private static partial void LogResourceMissing(ILogger logger, string virtualPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to stat physical path {PhysicalPath}")]
    private static partial void LogStatFailed(ILogger logger, Exception ex, string physicalPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse PROPFIND request body")]
    private static partial void LogPropFindBodyParseFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to query quota for {Root}")]
    private static partial void LogQuotaFailed(ILogger logger, Exception ex, string root);
}
