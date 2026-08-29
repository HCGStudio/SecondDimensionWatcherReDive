using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
internal partial class AuthController(
    IConfiguration configuration,
    TokenValidationParameters tokenValidationParams,
    IIdentityRepository identityRepository,
    SessionTokenIssuer tokenIssuer,
    ILogger<AuthController> logger) : ControllerBase
{
    [GeneratedRegex("^[a-z0-9._-]{3,64}$")]
    private static partial Regex UsernamePattern();

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] External.LoginData data,
        CancellationToken cancellationToken)
    {
        if (await identityRepository.AnyUsersAsync(cancellationToken)
            || HasLegacyPassword())
            return Conflict();
        if (!TryNormalizeUsername(data.Username, out var username)
            || string.IsNullOrEmpty(data.Password))
            return BadRequest();

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount(
            IdentityDefaults.UserId,
            username,
            BCrypt.Net.BCrypt.HashPassword(data.Password),
            UserRole.Admin,
            false,
            now,
            now);
        var profile = new UserProfile(
            IdentityDefaults.ProfileId,
            user.Id,
            NormalizeProfileName(data.ProfileName),
            null,
            null,
            true,
            now,
            now);
        try
        {
            await identityRepository.CreateUserWithProfileAsync(user, profile, cancellationToken);
        }
        catch (IdentityConflictException)
        {
            return Conflict();
        }
        return Ok(ToResult(await tokenIssuer.CreateSessionAsync(
            user, profile, data.DeviceName, cancellationToken)));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] External.LoginData data,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeUsername(data.Username, out var username))
            return Unauthorized();

        var user = await identityRepository.FindUserByUsernameAsync(username, cancellationToken);
        if (user is null
            && string.Equals(username, IdentityDefaults.Username, StringComparison.Ordinal)
            && VerifyLegacyPassword(data.Password))
            user = await CreateLegacyAdminAsync(data.Password, cancellationToken);
        if (user is null || user.IsDisabled || !await VerifyPasswordAsync(
                user, data.Password, cancellationToken))
            return Unauthorized();

        var profiles = await identityRepository.GetProfilesAsync(user.Id, cancellationToken);
        var profile = profiles.FirstOrDefault(candidate => candidate.IsDefault)
                      ?? profiles.FirstOrDefault();
        if (profile is null) return Unauthorized();

        return Ok(ToResult(await tokenIssuer.CreateSessionAsync(
            user, profile, data.DeviceName, cancellationToken)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] External.AuthRequest request,
        CancellationToken cancellationToken)
    {
        var principal = ValidateExpiredAccessToken(request.Token);
        if (principal is null
            || !principal.TryGetUserId(out var userId)
            || !principal.TryGetSessionId(out var sessionId))
            return Unauthorized(new External.LoginResult(null, null, false));

        var authenticated = await identityRepository.GetAuthenticatedSessionAsync(
            sessionId, DateTimeOffset.UtcNow, cancellationToken);
        if (authenticated is null || authenticated.User.Id != userId)
            return Unauthorized(new External.LoginResult(null, null, false));

        var rotated = await tokenIssuer.RotateSessionAsync(
            authenticated,
            authenticated.Profile,
            request.RefreshToken,
            reauthenticated: false,
            cancellationToken);
        return rotated is null
            ? Unauthorized(new External.LoginResult(null, null, false))
            : Ok(ToResult(rotated));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("reauthenticate")]
    public async Task<IActionResult> Reauthenticate(
        [FromBody] External.ReauthenticateRequest request,
        CancellationToken cancellationToken)
    {
        var authenticated = await GetCurrentSessionAsync(cancellationToken);
        if (authenticated is null
            || !await VerifyPasswordAsync(authenticated.User, request.Password, cancellationToken))
            return Unauthorized();

        var rotated = await tokenIssuer.RotateSessionAsync(
            authenticated,
            authenticated.Profile,
            request.RefreshToken,
            reauthenticated: true,
            cancellationToken);
        return rotated is null ? Unauthorized() : Ok(ToResult(rotated));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionId(out var sessionId)) return Unauthorized();
        await identityRepository.RevokeSessionAsync(
            sessionId,
            requiredUserId: null,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return NoContent();
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("verify")]
    public async Task<IActionResult> Verify(CancellationToken cancellationToken)
    {
        var authenticated = await GetCurrentSessionAsync(cancellationToken);
        if (authenticated is null) return Unauthorized();
        var profiles = await identityRepository.GetProfilesAsync(
            authenticated.User.Id, cancellationToken);
        return Ok(new External.AuthStateResponse(
            authenticated.User.Id,
            authenticated.User.Username,
            authenticated.User.Role.ToString(),
            authenticated.Session.Id,
            authenticated.Profile.Id,
            profiles.Select(ToProfileResponse).ToList()));
    }

    [HttpGet("allowRegister")]
    public async Task<IActionResult> CanRegister(CancellationToken cancellationToken) =>
        Ok(new
        {
            Allow = !HasLegacyPassword()
                    && !await identityRepository.AnyUsersAsync(cancellationToken)
        });

    private async Task<AuthenticatedSession?> GetCurrentSessionAsync(
        CancellationToken cancellationToken)
    {
        if (!User.TryGetSessionId(out var sessionId)) return null;
        return await identityRepository.GetAuthenticatedSessionAsync(
            sessionId, DateTimeOffset.UtcNow, cancellationToken);
    }

    private ClaimsPrincipal? ValidateExpiredAccessToken(string token)
    {
        try
        {
            var parameters = tokenValidationParams.Clone();
            parameters.ValidateLifetime = false;
            var principal = new JwtSecurityTokenHandler().ValidateToken(
                token, parameters, out var validatedToken);
            return validatedToken is JwtSecurityToken securityToken
                   && string.Equals(
                       securityToken.Header.Alg,
                       SecurityAlgorithms.HmacSha256,
                       StringComparison.OrdinalIgnoreCase)
                ? principal
                : null;
        }
        catch (Exception exception)
        {
            LogTokenVerificationFailed(logger, exception);
            return null;
        }
    }

    private async Task<UserAccount> CreateLegacyAdminAsync(
        string password,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount(
            IdentityDefaults.UserId,
            IdentityDefaults.Username,
            BCrypt.Net.BCrypt.HashPassword(password),
            UserRole.Admin,
            false,
            now,
            now);
        var profile = new UserProfile(
            IdentityDefaults.ProfileId,
            user.Id,
            IdentityDefaults.ProfileName,
            null,
            null,
            true,
            now,
            now);
        try
        {
            await identityRepository.CreateUserWithProfileAsync(user, profile, cancellationToken);
            return user;
        }
        catch (IdentityConflictException)
        {
            var existing = await identityRepository.FindUserByUsernameAsync(
                IdentityDefaults.Username, cancellationToken);
            if (existing is null) throw;
            return existing;
        }
    }

    private async Task<bool> VerifyPasswordAsync(
        UserAccount user,
        string password,
        CancellationToken cancellationToken)
    {
        if (user.PasswordHash is not null)
            return VerifyHash(password, user.PasswordHash);
        if (user.Id != IdentityDefaults.UserId || !VerifyLegacyPassword(password))
            return false;

        return await identityRepository.SetPasswordHashAsync(
            user.Id,
            BCrypt.Net.BCrypt.HashPassword(password),
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    private bool VerifyLegacyPassword(string password)
    {
        var value = configuration["Password:Value"];
        return !string.IsNullOrWhiteSpace(value) && VerifyHash(password, value);
    }

    private bool HasLegacyPassword() =>
        !string.IsNullOrWhiteSpace(configuration["Password:Value"]);

    private static bool VerifyHash(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private static bool TryNormalizeUsername(string? value, out string username)
    {
        username = string.IsNullOrWhiteSpace(value)
            ? IdentityDefaults.Username
            : value.Trim().ToLowerInvariant();
        return UsernamePattern().IsMatch(username);
    }

    private static string NormalizeProfileName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name)) return IdentityDefaults.ProfileName;
        return name.Length <= 64 ? name : name[..64];
    }

    private static External.LoginResult ToResult(IssuedSessionTokens tokens) =>
        new(tokens.AccessToken, tokens.RefreshToken, true, tokens.SessionId, tokens.ProfileId);

    internal static External.AuthProfileResponse ToProfileResponse(UserProfile profile) =>
        new(profile.Id, profile.Name, profile.Avatar, profile.PinHash is not null, profile.IsDefault);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token verification failed")]
    private static partial void LogTokenVerificationFailed(ILogger logger, Exception exception);
}
