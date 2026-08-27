namespace SecondDimensionWatcherReDive.Framework.Feed;

public sealed record SubscriptionReleaseMetadata(
    string? SubtitleGroup,
    string? Resolution,
    string? Codec,
    IReadOnlyList<string> Languages,
    long? SizeBytes);

public sealed record SubscriptionAutomationExplanation(
    string Field,
    bool Passed,
    string? Actual,
    string? Expected,
    string Message);

public sealed record SubscriptionAutomationEvaluation(
    bool Matched,
    SubscriptionReleaseMetadata Metadata,
    IReadOnlyList<SubscriptionAutomationExplanation> Explanations);

public sealed record SubscriptionAutomationSimulationEntry(
    string Id,
    string Title,
    DateTimeOffset PublishedAt,
    long? SizeBytes,
    bool Matched,
    IReadOnlyList<SubscriptionAutomationExplanation> Explanations);

public sealed record SubscriptionAutomationSimulationResult(
    int Total,
    int Matched,
    IReadOnlyList<SubscriptionAutomationSimulationEntry> Entries);
