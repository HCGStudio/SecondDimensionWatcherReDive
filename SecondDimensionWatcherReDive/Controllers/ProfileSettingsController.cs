using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/accounts/profiles/{id:guid}")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed partial class ProfileSettingsController(
    IIdentityRepository identities,
    IProfileSettingsRepository settings,
    IAuthorizationService authorization) : ControllerBase
{
    [GeneratedRegex("^[0-9]{4,8}$")]
    private static partial Regex PinPattern();

    [HttpPut("name")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> Rename(Guid id, External.RenameProfileRequest request,
        CancellationToken cancellationToken)
    {
        var (target, failure) = await EditableProfileAsync(id, cancellationToken);
        if (failure is not null) return failure;
        var name = request.Name.Trim();
        if (name.Length is < 1 or > 64) return BadRequest();
        try
        {
            return await settings.RenameAsync(id, target!.UserId, name, DateTimeOffset.UtcNow, cancellationToken)
                ? NoContent() : NotFound();
        }
        catch (IdentityConflictException) { return Conflict(); }
    }

    [HttpPut("pin")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> SetPin(Guid id, External.SetProfilePinRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        var target = await identities.FindProfileAsync(id, cancellationToken);
        if (target is null || target.UserId != userId) return NotFound();
        var pinVerified = false;
        if (target.PinHash is not null && request.CurrentPin is not null)
        {
            try { pinVerified = BCrypt.Net.BCrypt.Verify(request.CurrentPin, target.PinHash); }
            catch (BCrypt.Net.SaltParseException) { }
        }
        if (!pinVerified && !await RecentlyAuthenticatedAsync()) return Forbid();
        if (!string.IsNullOrEmpty(request.Pin) && !PinPattern().IsMatch(request.Pin)) return BadRequest();
        var hash = string.IsNullOrEmpty(request.Pin) ? null : BCrypt.Net.BCrypt.HashPassword(request.Pin);
        return await settings.SetPinAsync(id, userId, target.PinHash, hash, DateTimeOffset.UtcNow, cancellationToken)
            ? NoContent() : Conflict();
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> GetAvatar(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        var avatar = await settings.GetAvatarAsync(id, userId, cancellationToken);
        if (avatar is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(avatar.Data, avatar.ContentType);
    }

    [HttpPut("avatar")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    [RequestSizeLimit(ProfileAvatarUpload.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProfileAvatarUpload.MaxBytes)]
    public async Task<IActionResult> UploadAvatar(Guid id, IFormFile file,
        CancellationToken cancellationToken)
    {
        var (target, failure) = await EditableProfileAsync(id, cancellationToken);
        if (failure is not null) return failure;
        if (file.Length is <= 0 or > ProfileAvatarUpload.MaxBytes) return BadRequest();
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, cancellationToken);
        var data = buffer.ToArray();
        var contentType = ProfileAvatarUpload.GetContentType(data);
        if (contentType is null) return BadRequest(new External.MessageResponse("Upload a PNG or JPEG up to 2 MiB and 4096 × 4096 pixels."));
        return await settings.SetAvatarAsync(id, target!.UserId, new ProfileAvatar(data, contentType),
            DateTimeOffset.UtcNow, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpDelete("avatar")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> RemoveAvatar(Guid id, CancellationToken cancellationToken)
    {
        var (target, failure) = await EditableProfileAsync(id, cancellationToken);
        if (failure is not null) return failure;
        return await settings.SetAvatarAsync(id, target!.UserId, null, DateTimeOffset.UtcNow, cancellationToken)
            ? NoContent() : NotFound();
    }

    private async Task<(UserProfile? Profile, IActionResult? Failure)> EditableProfileAsync(
        Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetProfileId(out var activeId))
            return (null, Unauthorized());
        var profile = await identities.FindProfileAsync(id, cancellationToken);
        if (profile is null || profile.UserId != userId) return (null, NotFound());
        if (id != activeId && !await RecentlyAuthenticatedAsync()) return (null, Forbid());
        return (profile, null);
    }

    private async Task<bool> RecentlyAuthenticatedAsync() =>
        (await authorization.AuthorizeAsync(User, resource: null, AccessPolicies.RecentAuthentication)).Succeeded;
}
