namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record PlaybackProgress(
    Guid Id,
    Guid UserId,
    Guid AnimationInfoId,
    string VirtualPath,
    double PositionSeconds,
    double DurationSeconds,
    bool IsWatched,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WatchedAt);

public sealed record PlaybackPreferences(
    Guid UserId,
    string? SubtitleLanguage,
    string? SubtitleTrackLabel,
    string? AudioLanguage,
    string? AudioTrackLabel,
    bool AutoPlayNext,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A playable video mapping together with the metadata needed by playback clients.
/// VirtualPath is the canonical VFS path; callers translate it to the relative FileController path.
/// </summary>
public sealed record PlaybackMedia(
    Guid AnimationInfoId,
    string VirtualPath,
    string Path,
    string Title,
    Guid? AnimationId,
    string? AnimationName,
    string? PosterPath,
    Guid? GroupId,
    string? GroupName,
    int? Season,
    int? Episode,
    DateTimeOffset PublishTime);

public sealed record ContinueWatching(
    PlaybackProgress Progress,
    PlaybackMedia Media);

public sealed class PlaybackMappingChangedException()
    : InvalidOperationException("The playback file mapping changed while the state was being saved.");
