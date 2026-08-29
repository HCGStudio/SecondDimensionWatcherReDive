using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed partial class AccountsController(
    IIdentityRepository identityRepository,
    SessionTokenIssuer tokenIssuer,
    IAuthorizationService authorizationService) : ControllerBase
{
    [GeneratedRegex("^[a-z0-9._-]{3,64}$")]
    private static partial Regex UsernamePattern();

    [GeneratedRegex("^[0-9]{4,8}$")]
    private static partial Regex PinPattern();

    [HttpGet("profiles")]
    public async Task<IActionResult> GetProfiles(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        var profiles = await identityRepository.GetProfilesAsync(userId, cancellationToken);
        return Ok(profiles.Select(AuthController.ToProfileResponse).ToList());
    }

    [HttpPost("profiles")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> CreateProfile(
        [FromBody] External.CreateProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        if (!TryNormalizeProfile(request.Name, request.Avatar, request.Pin,
                out var name, out var avatar, out var pinHash))
            return BadRequest();
        var now = DateTimeOffset.UtcNow;
        UserProfile profile;
        try
        {
            profile = await identityRepository.AddProfileAsync(
                new UserProfile(
                    Guid.NewGuid(), userId, name, avatar, pinHash, false, now, now),
                cancellationToken);
        }
        catch (IdentityConflictException)
        {
            return Conflict();
        }
        return Ok(AuthController.ToProfileResponse(profile));
    }

    [HttpPatch("profiles/{id:guid}")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> UpdateProfile(
        [FromRoute] Guid id,
        [FromBody] External.UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        if (!User.TryGetProfileId(out var activeProfileId)) return Unauthorized();
        var target = await identityRepository.FindProfileAsync(id, cancellationToken);
        if (target is null || target.UserId != userId) return NotFound();

        var needsStepUp = id != activeProfileId || request.ReplacePin;
        if (needsStepUp)
        {
            var pinVerified = target.PinHash is not null
                              && VerifyPin(request.CurrentPin, target.PinHash);
            var recentlyAuthenticated = (await authorizationService.AuthorizeAsync(
                User, resource: null, AccessPolicies.RecentAuthentication)).Succeeded;
            if (!pinVerified && !recentlyAuthenticated) return Forbid();
        }

        if (!TryNormalizeProfile(
                request.Name,
                request.Avatar,
                request.ReplacePin ? request.Pin : null,
                out var name,
                out var avatar,
                out var pinHash))
            return BadRequest();
        bool updated;
        try
        {
            updated = await identityRepository.UpdateProfileAsync(
                id,
                userId,
                name,
                avatar,
                pinHash,
                request.ReplacePin,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (IdentityConflictException)
        {
            return Conflict();
        }
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("profiles/switch")]
    public async Task<IActionResult> SwitchProfile(
        [FromBody] External.SwitchProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)
            || !User.TryGetSessionId(out var sessionId))
            return Unauthorized();
        var profile = await identityRepository.FindProfileAsync(
            request.ProfileId, cancellationToken);
        if (profile is null || profile.UserId != userId)
            return NotFound();
        if (profile.PinHash is not null && !VerifyPin(request.Pin, profile.PinHash))
            return Unauthorized();

        var authenticated = await identityRepository.GetAuthenticatedSessionAsync(
            sessionId, DateTimeOffset.UtcNow, cancellationToken);
        if (authenticated is null || authenticated.User.Id != userId)
            return Unauthorized();
        var rotated = await tokenIssuer.RotateSessionAsync(
            authenticated,
            profile,
            request.RefreshToken,
            reauthenticated: false,
            cancellationToken);
        return rotated is null
            ? Unauthorized()
            : Ok(new External.LoginResult(
                rotated.AccessToken,
                rotated.RefreshToken,
                true,
                rotated.SessionId,
                rotated.ProfileId));
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetOwnSessions(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        User.TryGetSessionId(out var currentSessionId);
        var sessions = await identityRepository.GetSessionsAsync(userId, cancellationToken);
        return Ok(sessions.Select(session => ToResponse(session, currentSessionId)).ToList());
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeOwnSession(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId)) return Unauthorized();
        var revoked = await identityRepository.RevokeSessionAsync(
            id, userId, DateTimeOffset.UtcNow, cancellationToken);
        return revoked ? NoContent() : NotFound();
    }

    [HttpGet("users")]
    [Authorize(Policy = AccessPolicies.Administrator)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await identityRepository.GetUsersAsync(cancellationToken);
        return Ok(users.Select(ToResponse).ToList());
    }

    [HttpPost("users")]
    [Authorize(Policy = AccessPolicies.RecentAdministrator)]
    public async Task<IActionResult> CreateUser(
        [FromBody] External.CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        if (!UsernamePattern().IsMatch(username)
            || string.IsNullOrEmpty(request.Password)
            || !TryParseRole(request.Role, out var role)
            || !TryNormalizeProfile(request.ProfileName, null, null,
                out var profileName, out _, out _))
            return BadRequest();
        if (await identityRepository.FindUserByUsernameAsync(username, cancellationToken) is not null)
            return Conflict();

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount(
            Guid.NewGuid(),
            username,
            BCrypt.Net.BCrypt.HashPassword(request.Password),
            role,
            false,
            now,
            now);
        var profile = new UserProfile(
            Guid.NewGuid(),
            user.Id,
            profileName,
            null,
            null,
            true,
            now,
            now);
        try
        {
            return Ok(ToResponse(await identityRepository.CreateUserWithProfileAsync(
                user, profile, cancellationToken)));
        }
        catch (IdentityConflictException)
        {
            return Conflict();
        }
    }

    [HttpPatch("users/{id:guid}")]
    [Authorize(Policy = AccessPolicies.RecentAdministrator)]
    public async Task<IActionResult> UpdateUserAccess(
        [FromRoute] Guid id,
        [FromBody] External.UpdateUserAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseRole(request.Role, out var role)) return BadRequest();
        var result = await identityRepository.UpdateUserAccessAsync(
            id, role, request.IsDisabled, DateTimeOffset.UtcNow, cancellationToken);
        return result switch
        {
            UpdateUserAccessResult.Updated => NoContent(),
            UpdateUserAccessResult.NotFound => NotFound(),
            UpdateUserAccessResult.LastAdministrator => Conflict(new
            {
                message = "At least one enabled administrator is required."
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    }

    [HttpGet("sessions/all")]
    [Authorize(Policy = AccessPolicies.Administrator)]
    public async Task<IActionResult> GetAllSessions(CancellationToken cancellationToken)
    {
        User.TryGetSessionId(out var currentSessionId);
        var sessions = await identityRepository.GetSessionsAsync(null, cancellationToken);
        return Ok(sessions.Select(session => ToResponse(session, currentSessionId)).ToList());
    }

    [HttpDelete("sessions/{id:guid}/admin")]
    [Authorize(Policy = AccessPolicies.RecentAdministrator)]
    public async Task<IActionResult> RevokeAnySession(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var revoked = await identityRepository.RevokeSessionAsync(
            id, null, DateTimeOffset.UtcNow, cancellationToken);
        return revoked ? NoContent() : NotFound();
    }

    private static bool TryNormalizeProfile(
        string rawName,
        string? rawAvatar,
        string? rawPin,
        out string name,
        out string? avatar,
        out string? pinHash)
    {
        name = rawName.Trim();
        avatar = string.IsNullOrWhiteSpace(rawAvatar) ? null : rawAvatar.Trim();
        pinHash = null;
        if (name.Length is < 1 or > 64 || avatar?.Length > 512)
            return false;
        if (rawPin is null) return true;
        if (rawPin.Length == 0) return true;
        if (!PinPattern().IsMatch(rawPin)) return false;
        pinHash = BCrypt.Net.BCrypt.HashPassword(rawPin);
        return true;
    }

    private static bool VerifyPin(string? pin, string hash)
    {
        if (pin is null) return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(pin, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private static bool TryParseRole(string raw, out UserRole role) =>
        Enum.TryParse(raw, ignoreCase: true, out role)
        && Enum.IsDefined(role);

    private static External.UserResponse ToResponse(UserAccountWithProfiles item) =>
        new(item.User.Id,
            item.User.Username,
            item.User.Role.ToString(),
            item.User.IsDisabled,
            item.User.CreatedAt,
            item.Profiles.Select(AuthController.ToProfileResponse).ToList());

    private static External.SessionResponse ToResponse(
        UserSessionSummary item,
        Guid currentSessionId) =>
        new(item.Session.Id,
            item.Session.UserId,
            item.Username,
            item.Session.ActiveProfileId,
            item.ProfileName,
            item.Session.DeviceName,
            item.Session.AuthenticatedAt,
            item.Session.CreatedAt,
            item.Session.LastSeenAt,
            item.Session.ExpiresAt,
            item.Session.RevokedAt,
            item.Session.Id == currentSessionId);
}
