namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IPlaybackRepository
{
    Task<PlaybackProgress?> FindProgressAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlaybackProgress>> GetStatesAsync(
        Guid userId,
        Guid animationInfoId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ContinueWatching>> GetContinueWatchingAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken);

    Task<PlaybackProgress> UpsertProgressAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        double positionSeconds,
        double durationSeconds,
        bool markWatched,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<PlaybackProgress> SetWatchedAsync(
        Guid userId,
        Guid animationInfoId,
        string virtualPath,
        bool isWatched,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<PlaybackPreferences> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<PlaybackPreferences> UpsertPreferencesAsync(
        PlaybackPreferences preferences,
        CancellationToken cancellationToken);

    Task<PlaybackMedia?> GetNextMediaAsync(
        Guid animationInfoId,
        string virtualPath,
        CancellationToken cancellationToken);
}
