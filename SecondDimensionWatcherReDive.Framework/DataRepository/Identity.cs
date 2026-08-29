namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum UserRole
{
    Admin,
    Member,
    Viewer
}

public enum UpdateUserAccessResult
{
    Updated,
    NotFound,
    LastAdministrator
}

public sealed class IdentityConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public static class IdentityDefaults
{
    public static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid ProfileId = Guid.Empty;
    public const string Username = "admin";
    public const string ProfileName = "Home";
}

public sealed record UserAccount(
    Guid Id,
    string Username,
    string? PasswordHash,
    UserRole Role,
    bool IsDisabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UserProfile(
    Guid Id,
    Guid UserId,
    string Name,
    string? Avatar,
    string? PinHash,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UserSession(
    Guid Id,
    Guid UserId,
    Guid ActiveProfileId,
    string RefreshTokenHash,
    string? DeviceName,
    DateTimeOffset AuthenticatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt);

public sealed record AuthenticatedSession(
    UserAccount User,
    UserProfile Profile,
    UserSession Session);

public sealed record UserAccountWithProfiles(
    UserAccount User,
    IReadOnlyList<UserProfile> Profiles);

public sealed record UserSessionSummary(
    UserSession Session,
    string Username,
    string ProfileName);
