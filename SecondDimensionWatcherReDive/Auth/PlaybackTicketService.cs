using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using SecondDimensionWatcherReDive.Controllers.External;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed record PlaybackTicketBundle(
    string ResourceId,
    string CookieCredential,
    DateTimeOffset ExpiresAt);

internal sealed class PlaybackTicketService
{
    public const string SecureCookieName = "__Host-sdw-playback";
    public const string DevelopmentCookieName = "sdw-playback";
    private const int MaxCookieCredentialLength = 4096;
    private const int MaxResourceIdLength = 16 * 1024;
    private readonly IDataProtector _resourceProtector;
    private readonly IDataProtector _sessionProtector;
    private readonly IDeviceTokenHasher _tokenHasher;
    private readonly TimeProvider _timeProvider;

    public PlaybackTicketService(
        IDataProtectionProvider dataProtectionProvider,
        IDeviceTokenHasher tokenHasher,
        TimeProvider timeProvider)
    {
        _resourceProtector = dataProtectionProvider.CreateProtector(
            "SecondDimensionWatcherReDive.Playback.Resource.v1");
        _sessionProtector = dataProtectionProvider.CreateProtector(
            "SecondDimensionWatcherReDive.Playback.Session.v1");
        _tokenHasher = tokenHasher;
        _timeProvider = timeProvider;
    }

    public PlaybackTicketBundle Issue(
        string userId,
        string accessTokenId,
        string path,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessTokenId);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        var expiresAt = _timeProvider.GetUtcNow().Add(lifetime);
        // Every link generated concurrently from one access token receives the same session
        // binding. Different login/refresh sessions cannot borrow each other's cookie.
        var sessionId = _tokenHasher.Hash($"playback-session:{userId}:{accessTokenId}");
        var session = new PlaybackSessionTicket(userId, sessionId, expiresAt);
        var resource = new PlaybackResourceTicket(path, userId, sessionId, expiresAt);

        return new PlaybackTicketBundle(
            ProtectResource(resource),
            ProtectSession(session),
            expiresAt);
    }

    public PlaybackResourceTicket? Validate(string resourceId, string? cookieCredential)
    {
        if (string.IsNullOrWhiteSpace(resourceId)
            || resourceId.Length > MaxResourceIdLength
            || string.IsNullOrWhiteSpace(cookieCredential)
            || cookieCredential.Length > MaxCookieCredentialLength)
            return null;

        var resource = UnprotectResource(resourceId);
        var session = UnprotectSession(cookieCredential);
        if (resource is null || session is null)
            return null;

        var now = _timeProvider.GetUtcNow();
        if (resource.ExpiresAt <= now
            || session.ExpiresAt <= now
            || !string.Equals(resource.UserId, session.UserId, StringComparison.Ordinal)
            || !FixedTimeEquals(resource.SessionId, session.SessionId))
            return null;

        return resource;
    }

    private string ProtectResource(PlaybackResourceTicket resource)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            resource,
            AppJsonSerializerContext.Default.PlaybackResourceTicket);
        return WebEncoders.Base64UrlEncode(_resourceProtector.Protect(bytes));
    }

    private string ProtectSession(PlaybackSessionTicket session)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            session,
            AppJsonSerializerContext.Default.PlaybackSessionTicket);
        return WebEncoders.Base64UrlEncode(_sessionProtector.Protect(bytes));
    }

    private PlaybackResourceTicket? UnprotectResource(string resourceId)
    {
        try
        {
            var bytes = _resourceProtector.Unprotect(WebEncoders.Base64UrlDecode(resourceId));
            return JsonSerializer.Deserialize(
                bytes,
                AppJsonSerializerContext.Default.PlaybackResourceTicket);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private PlaybackSessionTicket? UnprotectSession(string credential)
    {
        try
        {
            var bytes = _sessionProtector.Unprotect(WebEncoders.Base64UrlDecode(credential));
            return JsonSerializer.Deserialize(
                bytes,
                AppJsonSerializerContext.Default.PlaybackSessionTicket);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
