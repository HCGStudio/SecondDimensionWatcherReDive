using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record PrepareTranscodingRequest(
    [Required] Guid Id,
    [Required] string Path,
    string? Quality,
    string? AudioLanguage,
    string? AudioTrackLabel,
    string? SubtitleLanguage,
    string? SubtitleTrackLabel);

internal sealed record TranscodingSubtitleResponse(
    string Path,
    string VirtualPath,
    string? Language,
    string Label,
    string Format,
    string Url);

internal sealed record TranscodingSessionResponse(
    Guid SessionId,
    string State,
    string? Strategy,
    bool IsPlayable,
    bool CacheHit,
    double? Progress,
    double? Speed,
    int? QueuePosition,
    string? Error,
    string? VideoCodec,
    string? AudioCodec,
    string StatusUrl,
    string CancelUrl,
    string? PlaybackUrl,
    IReadOnlyList<TranscodingSubtitleResponse> Subtitles,
    int UnsupportedSubtitleCount);

internal sealed record TranscodingMetricsResponse(
    int QueuedJobs,
    int ActiveJobs,
    long CompletedJobs,
    long FailedJobs,
    long CanceledJobs,
    long CacheHits,
    long CacheBytes,
    double? AverageFirstSegmentSeconds,
    double? AverageTranscodeSpeed,
    double FailureRate);
