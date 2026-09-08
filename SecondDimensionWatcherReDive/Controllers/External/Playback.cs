using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record PlaybackProgressRequest(
    [Required] Guid AnimationInfoId,
    [Required, StringLength(2048, MinimumLength = 1)] string Path,
    [Range(0d, 2678400d)] double PositionSeconds,
    [Range(0d, 2678400d)] double DurationSeconds,
    bool SuppressWatched = false);

internal sealed record PlaybackWatchedRequest(
    [Required] Guid AnimationInfoId,
    [Required, StringLength(2048, MinimumLength = 1)] string Path,
    bool IsWatched);

internal sealed record PlaybackPreferencesRequest(
    [StringLength(64)] string? SubtitleLanguage,
    [StringLength(128)] string? SubtitleTrackLabel,
    [StringLength(64)] string? AudioLanguage,
    [StringLength(128)] string? AudioTrackLabel,
    bool AutoPlayNext,
    bool AutoSkip = false);

internal sealed record PlaybackStateResponse(
    Guid AnimationInfoId,
    string Path,
    string VirtualPath,
    double PositionSeconds,
    double DurationSeconds,
    bool IsWatched,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? WatchedAt);

internal sealed record PlaybackPreferencesResponse(
    string? SubtitleLanguage,
    string? SubtitleTrackLabel,
    string? AudioLanguage,
    string? AudioTrackLabel,
    bool AutoPlayNext,
    DateTimeOffset? UpdatedAt,
    bool AutoSkip = false);

internal sealed record PlaybackMediaResponse(
    Guid AnimationInfoId,
    string Path,
    string VirtualPath,
    string Title,
    string? AnimationName,
    string? PosterPath,
    int? Season,
    int? Episode);

internal sealed record ContinueWatchingResponse(
    PlaybackMediaResponse Media,
    PlaybackStateResponse State);

internal sealed record ExternalSubtitleResponse(
    string Path,
    string VirtualPath,
    string? Language,
    string Label,
    string Format);

internal sealed record PlaybackContextResponse(
    PlaybackMediaResponse Media,
    PlaybackStateResponse? State,
    PlaybackPreferencesResponse Preferences,
    IReadOnlyList<ExternalSubtitleResponse> Subtitles,
    PlaybackMediaResponse? Next);
