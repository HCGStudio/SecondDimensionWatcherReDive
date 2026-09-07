namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IIdentityRepository
{
    Task<bool> AnyUsersAsync(CancellationToken cancellationToken);

    Task<UserAccount?> FindUserByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<UserAccount?> FindUserByUsernameAsync(
        string username,
        CancellationToken cancellationToken);

    Task<UserProfile?> FindProfileAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserProfile>> GetProfilesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<UserAccountWithProfiles> CreateUserWithProfileAsync(
        UserAccount user,
        UserProfile profile,
        CancellationToken cancellationToken);

    Task<bool> SetPasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<UserProfile> AddProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken);

    Task<bool> UpdateProfileAsync(
        Guid profileId,
        Guid userId,
        string name,
        string? avatar,
        string? pinHash,
        bool replacePin,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserAccountWithProfiles>> GetUsersAsync(
        CancellationToken cancellationToken);

    Task<UpdateUserAccessResult> UpdateUserAccessAsync(
        Guid userId,
        UserRole role,
        bool isDisabled,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task AddSessionAsync(UserSession session, CancellationToken cancellationToken);

    Task<AuthenticatedSession?> GetAuthenticatedSessionAsync(
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> TryRotateSessionAsync(
        Guid sessionId,
        string expectedRefreshTokenHash,
        string newRefreshTokenHash,
        Guid activeProfileId,
        DateTimeOffset? authenticatedAt,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSessionSummary>> GetSessionsAsync(
        Guid? userId,
        CancellationToken cancellationToken);

    Task<bool> RevokeSessionAsync(
        Guid sessionId,
        Guid? requiredUserId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken);
}
