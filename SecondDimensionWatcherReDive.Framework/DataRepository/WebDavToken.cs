namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record WebDavToken(
    Guid Id,
    Guid UserId,
    string Username,
    string TokenHash,
    string? Description,
    DateTimeOffset CreatedAt,
    string Scope,
    string VirtualRoot,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);
