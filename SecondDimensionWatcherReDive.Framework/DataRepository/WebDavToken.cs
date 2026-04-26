namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record WebDavToken(
    Guid Id,
    string Username,
    string TokenHash,
    string? Description,
    DateTimeOffset CreatedAt);
