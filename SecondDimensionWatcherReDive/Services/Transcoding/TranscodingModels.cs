using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal enum TranscodingJobState
{
    Queued,
    Probing,
    Transcoding,
    Ready,
    Failed,
    Canceled
}

internal enum TranscodingStrategy
{
    Direct,
    Remux,
    Transcode
}

internal sealed record TranscodingSelection(
    string Quality,
    string? AudioLanguage,
    string? AudioTrackLabel,
    string? SubtitleLanguage,
    string? SubtitleTrackLabel)
{
    public static TranscodingSelection Create(
        string? quality,
        string? audioLanguage,
        string? audioTrackLabel,
        string? subtitleLanguage,
        string? subtitleTrackLabel)
    {
        var normalizedQuality = string.IsNullOrWhiteSpace(quality)
            ? "auto"
            : quality.Trim().ToLowerInvariant();
        if (normalizedQuality is not ("auto" or "720p" or "1080p"))
            throw new ArgumentException("Quality must be auto, 720p, or 1080p.", nameof(quality));

        return new TranscodingSelection(
            normalizedQuality,
            Normalize(audioLanguage),
            Normalize(audioTrackLabel),
            Normalize(subtitleLanguage),
            Normalize(subtitleTrackLabel));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

internal sealed record TranscodingSource(
    Guid AnimationInfoId,
    Guid MappingId,
    string VirtualPath,
    string PhysicalPath,
    string FileStore,
    string FileName,
    long Length,
    DateTimeOffset LastModifiedUtc)
{
    public string BuildCacheKey(TranscodingSelection selection)
    {
        var material = string.Join('\n',
            MappingId.ToString("N"),
            VirtualPath,
            PhysicalPath,
            FileStore,
            Length.ToString(CultureInfo.InvariantCulture),
            LastModifiedUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            selection.Quality,
            selection.AudioLanguage ?? string.Empty,
            selection.AudioTrackLabel ?? string.Empty,
            selection.SubtitleLanguage ?? string.Empty,
            selection.SubtitleTrackLabel ?? string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

internal sealed record MediaStreamProbe(
    int Index,
    string CodecType,
    string CodecName,
    string? Language,
    string? Title,
    bool IsDefault,
    bool IsForced,
    bool IsAttachedPicture);

internal sealed record MediaProbe(
    string Container,
    TimeSpan? Duration,
    IReadOnlyList<MediaStreamProbe> Streams)
{
    public MediaStreamProbe? Video => Streams.FirstOrDefault(stream =>
        stream.CodecType == "video" && !stream.IsAttachedPicture);
}

internal sealed record TranscodingPlan(
    TranscodingStrategy Strategy,
    MediaStreamProbe Video,
    MediaStreamProbe? Audio,
    MediaStreamProbe? BitmapSubtitleToBurn,
    IReadOnlyList<MediaStreamProbe> TextSubtitles,
    int UnsupportedSubtitleCount,
    bool CopyVideo,
    bool CopyAudio);

internal sealed record TranscodingSubtitle(
    string FileName,
    string Label,
    string? Language,
    string Format);

internal sealed record TranscodingSessionStatus(
    Guid SessionId,
    string AccessToken,
    TranscodingJobState State,
    TranscodingStrategy? Strategy,
    bool IsPlayable,
    bool CacheHit,
    double? Progress,
    double? Speed,
    int? QueuePosition,
    string? Error,
    string? VideoCodec,
    string? AudioCodec,
    IReadOnlyList<TranscodingSubtitle> Subtitles,
    int UnsupportedSubtitleCount);

internal sealed record TranscodingContent(
    Stream Stream,
    string ContentType,
    string? FileName,
    long? Length,
    DateTimeOffset? LastModifiedUtc);

internal sealed record TranscodingMetricsSnapshot(
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

internal sealed class TranscodingQueueFullException()
    : InvalidOperationException("The transcoding queue is full. Try again later.");

internal sealed class TranscodingDisabledException()
    : InvalidOperationException("Server-side transcoding is disabled.");

internal sealed class TranscodingResourceLimitException(string message)
    : InvalidOperationException(message);
