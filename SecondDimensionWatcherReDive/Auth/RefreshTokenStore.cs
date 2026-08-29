using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed record RefreshTokenFamily(string FamilyId, DateTimeOffset ExpiresAtUtc);

internal sealed record IssuedRefreshToken(string Token, RefreshTokenFamily Family);

internal sealed record RefreshTokenState(
    string JwtId,
    string FamilyId,
    DateTimeOffset ExpiresAtUtc);

internal sealed class RefreshTokenStore(
    IDistributedCache cache,
    IOptions<TokenSecurityOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IssuedRefreshToken?> IssueAsync(
        string jwtId,
        RefreshTokenFamily? existingFamily,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var family = existingFamily ?? new RefreshTokenFamily(
            Guid.NewGuid().ToString("N"),
            now.AddDays(options.Value.RefreshTokenDays));
        if (family.ExpiresAtUtc <= now ||
            await cache.GetStringAsync(RevokedKey(family.FamilyId), cancellationToken) is not null)
            return null;

        var plaintext = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var state = new RefreshTokenState(jwtId, family.FamilyId, family.ExpiresAtUtc);
        await cache.SetStringAsync(
            ActiveKey(plaintext),
            JsonSerializer.Serialize(state, JsonOptions),
            CacheOptions(family.ExpiresAtUtc),
            cancellationToken);
        return new IssuedRefreshToken(plaintext, family);
    }

    public async Task<RefreshTokenFamily?> ConsumeAsync(
        string plaintext,
        string jwtId,
        CancellationToken cancellationToken)
    {
        var tokenKey = TokenFingerprint(plaintext);
        var gate = _locks.GetOrAdd(tokenKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var activeKey = ActiveKeyFromFingerprint(tokenKey);
            var serialized = await cache.GetStringAsync(activeKey, cancellationToken);
            if (serialized is null)
            {
                var replay = await ReadStateAsync(UsedKeyFromFingerprint(tokenKey), cancellationToken);
                if (replay is not null)
                    await RevokeFamilyAsync(replay, cancellationToken);
                return null;
            }

            var state = JsonSerializer.Deserialize<RefreshTokenState>(serialized, JsonOptions);
            if (state is null || state.ExpiresAtUtc <= timeProvider.GetUtcNow() ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state.JwtId),
                    Encoding.UTF8.GetBytes(jwtId)))
                return null;

            if (await cache.GetStringAsync(RevokedKey(state.FamilyId), cancellationToken) is not null)
            {
                await cache.RemoveAsync(activeKey, cancellationToken);
                return null;
            }

            await cache.RemoveAsync(activeKey, cancellationToken);
            await cache.SetStringAsync(
                UsedKeyFromFingerprint(tokenKey),
                serialized,
                CacheOptions(state.ExpiresAtUtc),
                cancellationToken);
            return new RefreshTokenFamily(state.FamilyId, state.ExpiresAtUtc);
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
                _locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(tokenKey, gate));
        }
    }

    public async Task RevokeAsync(string plaintext, CancellationToken cancellationToken)
    {
        var fingerprint = TokenFingerprint(plaintext);
        var activeKey = ActiveKeyFromFingerprint(fingerprint);
        var state = await ReadStateAsync(activeKey, cancellationToken)
                    ?? await ReadStateAsync(UsedKeyFromFingerprint(fingerprint), cancellationToken);
        if (state is null)
            return;

        await cache.RemoveAsync(activeKey, cancellationToken);
        await RevokeFamilyAsync(state, cancellationToken);
    }

    private async Task<RefreshTokenState?> ReadStateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var serialized = await cache.GetStringAsync(key, cancellationToken);
        return serialized is null
            ? null
            : JsonSerializer.Deserialize<RefreshTokenState>(serialized, JsonOptions);
    }

    private async Task RevokeFamilyAsync(
        RefreshTokenState state,
        CancellationToken cancellationToken)
    {
        if (state.ExpiresAtUtc <= timeProvider.GetUtcNow())
            return;
        await cache.SetStringAsync(
            RevokedKey(state.FamilyId),
            "1",
            CacheOptions(state.ExpiresAtUtc),
            cancellationToken);
    }

    private static DistributedCacheEntryOptions CacheOptions(DateTimeOffset expiresAtUtc) =>
        new() { AbsoluteExpiration = expiresAtUtc };

    private static string ActiveKey(string plaintext) =>
        ActiveKeyFromFingerprint(TokenFingerprint(plaintext));

    private static string ActiveKeyFromFingerprint(string fingerprint) =>
        $"auth:refresh:active:{fingerprint}";

    private static string UsedKeyFromFingerprint(string fingerprint) =>
        $"auth:refresh:used:{fingerprint}";

    private static string RevokedKey(string familyId) => $"auth:refresh:revoked:{familyId}";

    private static string TokenFingerprint(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
