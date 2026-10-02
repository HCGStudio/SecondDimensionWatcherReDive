namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record ProfileAvatar(byte[] Data, string ContentType);

public interface IProfileSettingsRepository
{
    Task<bool> RenameAsync(Guid profileId, Guid userId, string name,
        DateTimeOffset now, CancellationToken cancellationToken);

    Task<bool> SetPinAsync(Guid profileId, Guid userId, string? expectedPinHash,
        string? pinHash, DateTimeOffset now, CancellationToken cancellationToken);

    Task<ProfileAvatar?> GetAvatarAsync(Guid profileId, Guid userId,
        CancellationToken cancellationToken);

    Task<bool> SetAvatarAsync(Guid profileId, Guid userId, ProfileAvatar? avatar,
        DateTimeOffset now, CancellationToken cancellationToken);
}
