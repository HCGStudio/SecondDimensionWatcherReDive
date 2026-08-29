using System.Security.Claims;

namespace SecondDimensionWatcherReDive.Framework.Authorization;

public static class AccessPolicies
{
    public const string ContentWrite = nameof(ContentWrite);
    public const string PlaybackWrite = nameof(PlaybackWrite);
    public const string ChatWrite = nameof(ChatWrite);
    public const string Administrator = nameof(Administrator);
    public const string RecentAuthentication = nameof(RecentAuthentication);
    public const string RecentAdministrator = nameof(RecentAdministrator);
}

public static class IdentityClaimTypes
{
    public const string UserId = "userId";
    public const string ProfileId = "profileId";
    public const string SessionId = "sessionId";
    public const string AuthenticatedAt = "auth_time";
    public const string DeviceTokenId = "deviceTokenId";
    public const string DeviceScope = "deviceScope";
    public const string VirtualRoot = "virtualRoot";
}

public static class IdentityClaimsExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        TryGetGuid(principal, IdentityClaimTypes.UserId, out userId);

    public static bool TryGetProfileId(this ClaimsPrincipal principal, out Guid profileId) =>
        TryGetGuid(principal, IdentityClaimTypes.ProfileId, out profileId);

    public static bool TryGetSessionId(this ClaimsPrincipal principal, out Guid sessionId) =>
        TryGetGuid(principal, IdentityClaimTypes.SessionId, out sessionId);

    private static bool TryGetGuid(
        ClaimsPrincipal principal,
        string claimType,
        out Guid value) =>
        Guid.TryParse(principal.FindFirst(claimType)?.Value, out value);
}
