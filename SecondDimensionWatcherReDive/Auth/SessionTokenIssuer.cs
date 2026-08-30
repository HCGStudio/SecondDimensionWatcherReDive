using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed record IssuedSessionTokens(
    string AccessToken,
    string RefreshToken,
    Guid SessionId,
    Guid ProfileId);

internal sealed class SessionTokenIssuer(
    IConfiguration configuration,
    IIdentityRepository identityRepository)
{
    internal static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    public async Task<IssuedSessionTokens> CreateSessionAsync(
        UserAccount user,
        UserProfile profile,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshToken = GenerateRefreshToken();
        var session = new UserSession(
            Guid.NewGuid(),
            user.Id,
            profile.Id,
            HashRefreshToken(refreshToken),
            NormalizeDeviceName(deviceName),
            now,
            now,
            now,
            now + RefreshTokenLifetime,
            null);
        await identityRepository.AddSessionAsync(session, cancellationToken);
        return new IssuedSessionTokens(
            GenerateAccessToken(user, profile, session),
            refreshToken,
            session.Id,
            profile.Id);
    }

    public async Task<IssuedSessionTokens?> RotateSessionAsync(
        AuthenticatedSession authenticatedSession,
        UserProfile profile,
        string expectedRefreshToken,
        bool reauthenticated,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var refreshToken = GenerateRefreshToken();
        var authenticatedAt = reauthenticated ? now : (DateTimeOffset?)null;
        if (!await identityRepository.TryRotateSessionAsync(
                authenticatedSession.Session.Id,
                HashRefreshToken(expectedRefreshToken),
                HashRefreshToken(refreshToken),
                profile.Id,
                authenticatedAt,
                now,
                now + RefreshTokenLifetime,
                cancellationToken))
            return null;

        var session = authenticatedSession.Session with
        {
            ActiveProfileId = profile.Id,
            RefreshTokenHash = HashRefreshToken(refreshToken),
            AuthenticatedAt = authenticatedAt ?? authenticatedSession.Session.AuthenticatedAt,
            LastSeenAt = now,
            ExpiresAt = now + RefreshTokenLifetime
        };
        return new IssuedSessionTokens(
            GenerateAccessToken(authenticatedSession.User, profile, session),
            refreshToken,
            session.Id,
            profile.Id);
    }

    public static string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private string GenerateAccessToken(
        UserAccount user,
        UserProfile profile,
        UserSession session)
    {
        var key = Encoding.ASCII.GetBytes(configuration["JwtSecret"]!);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(IdentityClaimTypes.UserId, user.Id.ToString()),
                new Claim(IdentityClaimTypes.ProfileId, profile.Id.ToString()),
                new Claim(IdentityClaimTypes.SessionId, session.Id.ToString()),
                new Claim(IdentityClaimTypes.AuthenticatedAt,
                    session.AuthenticatedAt.ToUnixTimeSeconds().ToString()),
                new Claim("Id", profile.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Expires = DateTime.UtcNow.Add(AccessTokenLifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? NormalizeDeviceName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }
}
