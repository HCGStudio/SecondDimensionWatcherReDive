using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using ProfileEntity = SecondDimensionWatcherReDive.Models.UserProfile;
using SessionEntity = SecondDimensionWatcherReDive.Models.LoginSession;
using UserEntity = SecondDimensionWatcherReDive.Models.UserAccount;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class IdentityRepository(Models.ApplicationContext context) : IIdentityRepository
{
    public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) =>
        context.Users.AnyAsync(cancellationToken);

    public async Task<UserAccount?> FindUserByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        (await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken))?.ToRecord();

    public async Task<UserAccount?> FindUserByUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        var normalized = username.Trim().ToLowerInvariant();
        return (await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Username == normalized, cancellationToken))?.ToRecord();
    }

    public async Task<UserProfile?> FindProfileAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        (await context.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken))?.ToRecord();

    public async Task<IReadOnlyList<UserProfile>> GetProfilesAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        (await context.Profiles.AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name)
            .ToListAsync(cancellationToken))
        .Select(profile => profile.ToRecord())
        .ToList();

    public async Task<UserAccountWithProfiles> CreateUserWithProfileAsync(
        UserAccount user,
        UserProfile profile,
        CancellationToken cancellationToken)
    {
        context.Users.Add(user.ToEntity());
        context.Profiles.Add(profile.ToEntity());
        // A single SaveChanges call is transactionally atomic and is executed by
        // Npgsql's configured retry strategy. An explicit user transaction here
        // would be rejected by EnableRetryOnFailure in production.
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            context.ChangeTracker.Clear();
            throw new IdentityConflictException(
                "A user or profile with the same identity already exists.", exception);
        }
        return new UserAccountWithProfiles(user, [profile]);
    }

    public async Task<bool> SetPasswordHashAsync(
        Guid userId,
        string passwordHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var affected = await context.Users
            .Where(user => user.Id == userId && !user.IsDisabled)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.PasswordHash, passwordHash)
                .SetProperty(user => user.UpdatedAt, now), cancellationToken);
        return affected == 1;
    }

    public async Task<UserProfile> AddProfileAsync(
        UserProfile profile,
        CancellationToken cancellationToken)
    {
        context.Profiles.Add(profile.ToEntity());
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            context.ChangeTracker.Clear();
            throw new IdentityConflictException(
                "A profile with the same name already exists for this user.", exception);
        }
        return profile;
    }

    public async Task<bool> UpdateProfileAsync(
        Guid profileId,
        Guid userId,
        string name,
        string? avatar,
        string? pinHash,
        bool replacePin,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        int affected;
        try
        {
            affected = await context.Profiles
                .Where(profile => profile.Id == profileId && profile.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(profile => profile.Name, name)
                    .SetProperty(profile => profile.Avatar, avatar)
                    .SetProperty(profile => profile.PinHash,
                        profile => replacePin ? pinHash : profile.PinHash)
                    .SetProperty(profile => profile.UpdatedAt, now), cancellationToken);
        }
        catch (Exception exception) when (IsUniqueViolation(exception))
        {
            throw new IdentityConflictException(
                "A profile with the same name already exists for this user.", exception);
        }
        return affected == 1;
    }

    public async Task<IReadOnlyList<UserAccountWithProfiles>> GetUsersAsync(
        CancellationToken cancellationToken)
    {
        var users = await context.Users.AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);
        var profiles = await context.Profiles.AsNoTracking()
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name)
            .ToListAsync(cancellationToken);
        var byUser = profiles.ToLookup(profile => profile.UserId);
        return users.Select(user => new UserAccountWithProfiles(
                user.ToRecord(),
                byUser[user.Id].Select(profile => profile.ToRecord()).ToList()))
            .ToList();
    }

    public async Task<UpdateUserAccessResult> UpdateUserAccessAsync(
        Guid userId,
        UserRole role,
        bool isDisabled,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            // Serialize the household-admin invariant across independent users. A row lock on
            // only the target cannot prevent two administrators from demoting each other.
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(600000000000000017)",
                cancellationToken);

            var target = await context.Users.FirstOrDefaultAsync(
                user => user.Id == userId, cancellationToken);
            if (target is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return UpdateUserAccessResult.NotFound;
            }

            if (target.Role == UserRole.Admin
                && !target.IsDisabled
                && (role != UserRole.Admin || isDisabled)
                && !await context.Users.AnyAsync(
                    user => user.Id != userId
                            && user.Role == UserRole.Admin
                            && !user.IsDisabled,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return UpdateUserAccessResult.LastAdministrator;
            }

            var accessChanged = target.Role != role || target.IsDisabled != isDisabled;
            target.Role = role;
            target.IsDisabled = isDisabled;
            target.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            if (accessChanged)
            {
                await context.LoginSessions
                    .Where(session => session.UserId == userId && session.RevokedAt == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(session => session.RevokedAt, now), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return UpdateUserAccessResult.Updated;
        });
    }

    public async Task AddSessionAsync(
        UserSession session,
        CancellationToken cancellationToken)
    {
        context.LoginSessions.Add(session.ToEntity());
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthenticatedSession?> GetAuthenticatedSessionAsync(
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entity = await context.LoginSessions.AsNoTracking()
            .Include(session => session.User)
            .Include(session => session.ActiveProfile)
            .FirstOrDefaultAsync(session => session.Id == sessionId
                                            && session.RevokedAt == null
                                            && session.ExpiresAt > now
                                            && !session.User.IsDisabled,
                cancellationToken);
        if (entity is null || entity.ActiveProfile.UserId != entity.UserId)
            return null;
        return new AuthenticatedSession(
            entity.User.ToRecord(),
            entity.ActiveProfile.ToRecord(),
            entity.ToRecord());
    }

    public async Task<bool> TryRotateSessionAsync(
        Guid sessionId,
        string expectedRefreshTokenHash,
        string newRefreshTokenHash,
        Guid activeProfileId,
        DateTimeOffset? authenticatedAt,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var affected = await context.LoginSessions
            .Where(session => session.Id == sessionId
                              && session.RefreshTokenHash == expectedRefreshTokenHash
                              && session.RevokedAt == null
                              && session.ExpiresAt > now
                              && context.Profiles.Any(profile =>
                                  profile.Id == activeProfileId
                                  && profile.UserId == session.UserId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.RefreshTokenHash, newRefreshTokenHash)
                .SetProperty(session => session.ActiveProfileId, activeProfileId)
                .SetProperty(session => session.AuthenticatedAt,
                    session => authenticatedAt ?? session.AuthenticatedAt)
                .SetProperty(session => session.LastSeenAt, now)
                .SetProperty(session => session.ExpiresAt, expiresAt), cancellationToken);
        return affected == 1;
    }

    public async Task<IReadOnlyList<UserSessionSummary>> GetSessionsAsync(
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var query = context.LoginSessions.AsNoTracking().AsQueryable();
        if (userId.HasValue)
            query = query.Where(session => session.UserId == userId.Value);
        return await query
            .OrderByDescending(session => session.LastSeenAt)
            .Select(session => new UserSessionSummary(
                new UserSession(
                    session.Id,
                    session.UserId,
                    session.ActiveProfileId,
                    string.Empty,
                    session.DeviceName,
                    session.AuthenticatedAt,
                    session.CreatedAt,
                    session.LastSeenAt,
                    session.ExpiresAt,
                    session.RevokedAt),
                session.User.Username,
                session.ActiveProfile.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RevokeSessionAsync(
        Guid sessionId,
        Guid? requiredUserId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var affected = await context.LoginSessions
            .Where(session => session.Id == sessionId
                              && session.RevokedAt == null
                              && (!requiredUserId.HasValue
                                  || session.UserId == requiredUserId.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.RevokedAt, revokedAt), cancellationToken);
        return affected == 1;
    }

    private static bool IsUniqueViolation(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        || exception.InnerException is not null && IsUniqueViolation(exception.InnerException);

}

internal static class IdentityRepositoryConverter
{
    internal static UserAccount ToRecord(this UserEntity entity) => new(
        entity.Id,
        entity.Username,
        entity.PasswordHash,
        entity.Role,
        entity.IsDisabled,
        entity.CreatedAt,
        entity.UpdatedAt);

    internal static UserProfile ToRecord(this ProfileEntity entity) => new(
        entity.Id,
        entity.UserId,
        entity.Name,
        entity.Avatar,
        entity.PinHash,
        entity.IsDefault,
        entity.CreatedAt,
        entity.UpdatedAt);

    internal static UserSession ToRecord(this SessionEntity entity) => new(
        entity.Id,
        entity.UserId,
        entity.ActiveProfileId,
        entity.RefreshTokenHash,
        entity.DeviceName,
        entity.AuthenticatedAt,
        entity.CreatedAt,
        entity.LastSeenAt,
        entity.ExpiresAt,
        entity.RevokedAt);

    internal static UserEntity ToEntity(this UserAccount record) => new()
    {
        Id = record.Id,
        Username = record.Username,
        PasswordHash = record.PasswordHash,
        Role = record.Role,
        IsDisabled = record.IsDisabled,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };

    internal static ProfileEntity ToEntity(this UserProfile record) => new()
    {
        Id = record.Id,
        UserId = record.UserId,
        Name = record.Name,
        Avatar = record.Avatar,
        PinHash = record.PinHash,
        IsDefault = record.IsDefault,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };

    internal static SessionEntity ToEntity(this UserSession record) => new()
    {
        Id = record.Id,
        UserId = record.UserId,
        ActiveProfileId = record.ActiveProfileId,
        RefreshTokenHash = record.RefreshTokenHash,
        DeviceName = record.DeviceName,
        AuthenticatedAt = record.AuthenticatedAt,
        CreatedAt = record.CreatedAt,
        LastSeenAt = record.LastSeenAt,
        ExpiresAt = record.ExpiresAt,
        RevokedAt = record.RevokedAt
    };
}
