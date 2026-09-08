using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record LogicalDataExportEnvelope(
    LogicalDataBundle Data,
    string Sha256);

internal sealed record LogicalDataImportRequest(
    [property: Required] LogicalDataBundle? Data,
    [property: Required] string? Sha256,
    [property: Required] LogicalImportConflictStrategy? ConflictStrategy);

// Keep the original property order, enum names and payload shape for format-1
// checksum canonicalization. Playback preferences use their shared type so its
// existing optional properties retain their original serialization behavior.
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<LogicalDataCategoryV1>))]
internal enum LogicalDataCategoryV1
{
    None = 0,
    Feeds = 1,
    AutomationPolicies = 2,
    FileNameRules = 4,
    MetadataCorrections = 8,
    Playback = 16,
    All = Feeds | AutomationPolicies | FileNameRules | MetadataCorrections | Playback
}

internal sealed record LogicalDataBundleV1(
    int FormatVersion,
    DateTimeOffset ExportedAtUtc,
    string ApplicationVersion,
    LogicalDataCategoryV1 Categories,
    IReadOnlyList<LogicalFeed> Feeds,
    IReadOnlyList<LogicalAutomationPolicy> AutomationPolicies,
    IReadOnlyList<LogicalFileNameRule> FileNameRules,
    IReadOnlyList<LogicalMetadataCorrection> MetadataCorrections,
    IReadOnlyList<LogicalPlaybackProgress> PlaybackProgress,
    LogicalPlaybackPreferences? PlaybackPreferences);
