namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
///     Per-feed rules used to decide whether and how a newly discovered release is handled.
///     Empty preference lists mean that the corresponding attribute is unrestricted.
/// </summary>
public sealed record SubscriptionAutomationPolicy(
    Guid FeedId,
    IReadOnlyList<string> SubtitleGroups,
    IReadOnlyList<string> Resolutions,
    IReadOnlyList<string> Codecs,
    IReadOnlyList<string> Languages,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> ExcludedKeywords,
    SubscriptionAutomationMode Mode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool EnableVersionUpgrade = false,
    int MinimumUpgradeScore = 25,
    int UpgradeRollbackHours = 72);
