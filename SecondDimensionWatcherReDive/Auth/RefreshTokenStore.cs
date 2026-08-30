using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed record IssuedRefreshToken(
    string Token,
    string JwtId);

internal sealed record RefreshTokenState(
    string JwtId,
    string FamilyId,
    long ExpiresAtUnixTimeMilliseconds);

internal sealed record RefreshTokenReplacement(
    string PreviousJwtId,
    string ReplacementJwtId,
    string ReplacementToken,
    string FamilyId,
    long ExpiresAtUnixTimeMilliseconds);

internal sealed class RefreshTokenStore(
    IRefreshTokenStorage storage,
    IOptions<TokenSecurityOptions> options,
    TimeProvider timeProvider)
{
    public async Task<IssuedRefreshToken?> IssueAsync(
        string jwtId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var familyId = Guid.NewGuid().ToString("N");
        var expiresAtUtc = now.AddDays(options.Value.RefreshTokenDays);

        var plaintext = CreateToken();
        var state = new RefreshTokenState(
            jwtId,
            familyId,
            expiresAtUtc.ToUnixTimeMilliseconds());
        var created = await storage.TryCreateAsync(
            TokenFingerprint(plaintext),
            state,
            now,
            cancellationToken);
        return created ? new IssuedRefreshToken(plaintext, jwtId) : null;
    }

    public async Task<IssuedRefreshToken?> RotateAsync(
        string plaintext,
        string jwtId,
        string replacementJwtId,
        CancellationToken cancellationToken)
    {
        var replacementToken = CreateToken();
        var replacement = await storage.RotateAsync(
            TokenFingerprint(plaintext),
            jwtId,
            TokenFingerprint(replacementToken),
            replacementJwtId,
            replacementToken,
            timeProvider.GetUtcNow(),
            TimeSpan.FromSeconds(options.Value.RefreshTokenReuseGraceSeconds),
            cancellationToken);
        if (replacement is null)
            return null;

        return new IssuedRefreshToken(
            replacement.ReplacementToken,
            replacement.ReplacementJwtId);
    }

    public Task RevokeAsync(string plaintext, CancellationToken cancellationToken) =>
        storage.RevokeAsync(
            TokenFingerprint(plaintext),
            timeProvider.GetUtcNow(),
            cancellationToken);

    private static string CreateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string TokenFingerprint(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
