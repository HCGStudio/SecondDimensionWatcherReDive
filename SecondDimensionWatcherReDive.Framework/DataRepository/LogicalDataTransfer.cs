using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public static class LogicalDataTransferLimits
{
    public const int MaximumItemsPerCategory = 10_000;
    public const int MaximumPayloadBytes = 10 * 1024 * 1024;
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<LogicalDataCategory>))]
public enum LogicalDataCategory
{
    None = 0,
    Feeds = 1,
    AutomationPolicies = 2,
    FileNameRules = 4,
    MetadataCorrections = 8,
    Playback = 16,
    All = Feeds | AutomationPolicies | FileNameRules | MetadataCorrections | Playback
}

[JsonConverter(typeof(JsonStringEnumConverter<LogicalImportConflictStrategy>))]
public enum LogicalImportConflictStrategy
{
    Skip,
    Overwrite,
    Fail
}

public sealed record LogicalFeed(
    Guid Id,
    string Url,
    string? Name,
    DateTimeOffset CreatedAt);

public sealed record LogicalAutomationPolicy(
    string FeedUrl,
    IReadOnlyList<string> SubtitleGroups,
    IReadOnlyList<string> Resolutions,
    IReadOnlyList<string> Codecs,
    IReadOnlyList<string> Languages,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> ExcludedKeywords,
    SubscriptionAutomationMode Mode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LogicalFileNameRule(
    Guid Id,
    string AnimationTmdbId,
    string AnimationName,
    string AnimationOriginalName,
    string? AnimationPosterPath,
    string Pattern,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record LogicalMetadataCorrection(
    Guid OperationId,
    string ReleaseDownloadUrl,
    string ReleaseTitle,
    DateTimeOffset ReleasePublishTime,
    string AnimationTmdbId,
    string AnimationName,
    string AnimationOriginalName,
    string? AnimationPosterPath,
    string Description,
    int? Season,
    int? Episode,
    string? GroupName,
    DateTimeOffset AppliedAt);

public sealed record LogicalPlaybackProgress(
    string VirtualPath,
    double PositionSeconds,
    double DurationSeconds,
    bool IsWatched,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WatchedAt);

public sealed record LogicalPlaybackPreferences(
    string? SubtitleLanguage,
    string? SubtitleTrackLabel,
    string? AudioLanguage,
    string? AudioTrackLabel,
    bool AutoPlayNext,
    DateTimeOffset UpdatedAt);

public sealed record LogicalDataBundle(
    int FormatVersion,
    DateTimeOffset ExportedAtUtc,
    string ApplicationVersion,
    LogicalDataCategory Categories,
    IReadOnlyList<LogicalFeed> Feeds,
    IReadOnlyList<LogicalAutomationPolicy> AutomationPolicies,
    IReadOnlyList<LogicalFileNameRule> FileNameRules,
    IReadOnlyList<LogicalMetadataCorrection> MetadataCorrections,
    IReadOnlyList<LogicalPlaybackProgress> PlaybackProgress,
    LogicalPlaybackPreferences? PlaybackPreferences);

public sealed record LogicalImportResult(
    int Added,
    int Updated,
    int Skipped,
    int Conflicts,
    IReadOnlyList<string> Messages);

public sealed class LogicalDataImportConflictException(string message) : InvalidOperationException(message);

public sealed class LogicalDataExportLimitException(string message) : InvalidOperationException(message);
