using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class NativeDownloadController(
    IFileMappingRepository mappings,
    IFileStoreProvider stores,
    IIdentityRepository identities,
    PlaybackTicketService tickets,
    IContentTypeProvider contentTypes,
    IOptions<TokenSecurityOptions> options) : ControllerBase
{
    [HttpPost("api/vfs/download-link")]
    public async Task<IActionResult> Create([FromQuery] string path, CancellationToken cancellationToken)
    {
        SetHeaders();
        if (!User.TryGetUserId(out var userId) || !User.TryGetProfileId(out var profileId)
            || !User.TryGetSessionId(out var sessionId)) return Unauthorized();
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(jti)) return Unauthorized();
        if (!DevicePathScope.TryMapPublicToInternal(path, DevicePathScope.GetVirtualRoot(User), out _, out var internalPath))
            return BadRequest();
        var entry = await mappings.FindFileSystemEntryAsync(internalPath, cancellationToken);
        if (entry?.Mapping is not { } mapping || entry.IsDirectory) return NotFound();
        FileStoreInfo metadata;
        try { metadata = await stores.GetRequiredClient(mapping.FileStore).FileInfoAsync(mapping.PhysicalPath, cancellationToken); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return NotFound(); }
        if (metadata.IsDirectory) return NotFound();
        var lifetime = TimeSpan.FromMinutes(options.Value.PlaybackLinkMinutes);
        var bundle = tickets.Issue(userId.ToString(), jti, mapping.VirtualPath, lifetime, sessionId, profileId,
            Fingerprint(mapping, metadata), "download");
        Response.Cookies.Append(Request.IsHttps ? PlaybackTicketService.SecureCookieName : PlaybackTicketService.DevelopmentCookieName,
            bundle.CookieCredential, new CookieOptions
            {
                HttpOnly = true, Secure = Request.IsHttps, SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                Path = Request.IsHttps ? "/" : "/api/file/play", MaxAge = lifetime, IsEssential = true
            });
        return Ok(new External.FileLinkResultResponse(Url.ActionLink(nameof(Download), values: new { resourceId = bundle.ResourceId })!));
    }

    [AllowAnonymous]
    [HttpGet("api/file/play/download/{resourceId}")]
    [HttpHead("api/file/play/download/{resourceId}")]
    public async Task<IActionResult> Download(string resourceId, CancellationToken cancellationToken)
    {
        SetHeaders();
        var credential = Request.Cookies[PlaybackTicketService.SecureCookieName]
                         ?? Request.Cookies[PlaybackTicketService.DevelopmentCookieName];
        var grant = tickets.Validate(resourceId, credential);
        if (grant is not { Purpose: "download", IdentitySessionId: { } sessionId, ProfileId: { } profileId }) return NotFound();
        // Recheck the live session on every request, including HEAD and every resumed range.
        var session = await identities.GetAuthenticatedSessionAsync(sessionId, DateTimeOffset.UtcNow, cancellationToken);
        if (session is null || session.User.Id.ToString() != grant.UserId || session.Profile.Id != profileId) return NotFound();
        var entry = await mappings.FindFileSystemEntryAsync(grant.Path, cancellationToken);
        if (entry?.Mapping is not { } mapping || entry.IsDirectory) return NotFound();
        var store = stores.GetRequiredClient(mapping.FileStore);
        FileStoreInfo metadata;
        try { metadata = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return NotFound(); }
        if (metadata.IsDirectory || Fingerprint(mapping, metadata) != grant.MappingFingerprint) return NotFound();
        // Open the validated physical file directly: a concurrent virtual remap cannot redirect this grant.
        Stream stream;
        try { stream = await store.OpenReadStreamAsync(mapping.PhysicalPath, cancellationToken); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return NotFound(); }
        if (!stream.CanSeek && metadata.Length.HasValue) Response.ContentLength = metadata.Length.Value;
        var filename = Path.GetFileName(mapping.VirtualPath);
        var contentType = contentTypes.TryGetContentType(filename, out var type) ? type : "application/octet-stream";
        // Resuming requires a physical version, not just the identity of the virtual mapping.
        var hasStableVersion = metadata.Length.HasValue && metadata.LastModifiedUtc.HasValue;
        return new FileStreamResult(stream, contentType)
        {
            FileDownloadName = filename,
            LastModified = hasStableVersion ? metadata.LastModifiedUtc : null,
            EntityTag = hasStableVersion ? new EntityTagHeaderValue($"\"{grant.MappingFingerprint}\"") : null,
            EnableRangeProcessing = stream.CanSeek && hasStableVersion
        };
    }

    private void SetHeaders()
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static string Fingerprint(FileMapping mapping, FileStoreInfo metadata) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{mapping.Id}\0{mapping.AnimationInfoId}\0{mapping.VirtualPath}\0{mapping.FileStore}\0{mapping.PhysicalPath}\0{metadata.Length}\0{metadata.LastModifiedUtc:O}")));
}
