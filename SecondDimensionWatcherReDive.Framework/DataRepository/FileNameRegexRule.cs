namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>
///     A reusable regular expression for extracting season and episode numbers from file names.
///     Rules must contain an <c>episode</c> named capture group and may contain a <c>season</c> group.
/// </summary>
public sealed record FileNameRegexRule(
    Guid Id,
    Guid AnimationId,
    string Pattern,
    string? Description,
    DateTimeOffset CreatedAt);
