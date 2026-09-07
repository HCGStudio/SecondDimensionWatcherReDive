namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record UpsertSubscriptionAutomationPolicyRequest(
    IReadOnlyList<string>? SubtitleGroups,
    IReadOnlyList<string>? Resolutions,
    IReadOnlyList<string>? Codecs,
    IReadOnlyList<string>? Languages,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string>? ExcludedKeywords,
    string Mode,
    bool EnableVersionUpgrade = false,
    int? MinimumUpgradeScore = null,
    int? UpgradeRollbackHours = null);

internal sealed record SubscriptionAutomationPolicy(
    Guid FeedId,
    IReadOnlyList<string> SubtitleGroups,
    IReadOnlyList<string> Resolutions,
    IReadOnlyList<string> Codecs,
    IReadOnlyList<string> Languages,
    long? MinSizeBytes,
    long? MaxSizeBytes,
    IReadOnlyList<string> ExcludedKeywords,
    string Mode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool EnableVersionUpgrade,
    int MinimumUpgradeScore,
    int UpgradeRollbackHours);

internal sealed record SubscriptionAutomationExplanation(
    string Field,
    bool Passed,
    string? Actual,
    string? Expected,
    string Message);

internal sealed record SubscriptionAutomationSimulationEntry(
    string Id,
    string Title,
    DateTimeOffset PublishedAt,
    long? SizeBytes,
    bool Matched,
    IReadOnlyList<SubscriptionAutomationExplanation> Explanations);

internal sealed record SubscriptionAutomationSimulationResult(
    int Total,
    int Matched,
    IReadOnlyList<SubscriptionAutomationSimulationEntry> Entries);
