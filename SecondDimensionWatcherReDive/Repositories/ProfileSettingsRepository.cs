using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using AvatarEntity = SecondDimensionWatcherReDive.Models.ProfileAvatar;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class ProfileSettingsRepository(Models.ApplicationContext context) : IProfileSettingsRepository
{
    public async Task<bool> RenameAsync(Guid profileId, Guid userId, string name,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            return await context.Profiles.Where(profile => profile.Id == profileId && profile.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.Name, name)
                    .SetProperty(profile => profile.UpdatedAt, now), cancellationToken) == 1;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new IdentityConflictException("A profile with the same name already exists.", exception);
        }
    }

    public async Task<bool> SetPinAsync(Guid profileId, Guid userId, string? expectedPinHash,
        string? pinHash, DateTimeOffset now, CancellationToken cancellationToken) =>
        await context.Profiles.Where(profile => profile.Id == profileId && profile.UserId == userId
                && profile.PinHash == expectedPinHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.PinHash, pinHash)
                .SetProperty(profile => profile.UpdatedAt, now), cancellationToken) == 1;

    public Task<ProfileAvatar?> GetAvatarAsync(Guid profileId, Guid userId,
        CancellationToken cancellationToken) =>
        (from avatar in context.Set<AvatarEntity>().AsNoTracking()
         join profile in context.Profiles on avatar.ProfileId equals profile.Id
         where profile.Id == profileId && profile.UserId == userId
         select new ProfileAvatar(avatar.Data, avatar.ContentType)).SingleOrDefaultAsync(cancellationToken);

    public Task<bool> SetAvatarAsync(Guid profileId, Guid userId, ProfileAvatar? avatar,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var path = avatar is null ? null : $"/api/accounts/profiles/{profileId}/avatar?v={Guid.NewGuid():N}";
            // Updating the profile first serializes concurrent uploads/removals on its row.
            var updated = await context.Profiles.Where(profile => profile.Id == profileId && profile.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.Avatar, path)
                    .SetProperty(profile => profile.UpdatedAt, now), cancellationToken);
            if (updated == 0) return false;
            await context.Set<AvatarEntity>().Where(item => item.ProfileId == profileId)
                .ExecuteDeleteAsync(cancellationToken);
            if (avatar is not null)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ProfileAvatars" ("ProfileId", "Data", "ContentType")
                    VALUES ({profileId}, {avatar.Data}, {avatar.ContentType})
                    """, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
}
