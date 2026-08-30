namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record WebDavTokenSummary(
    Guid Id,
    Guid UserId,
    string Username,
    string? Description,
    DateTimeOffset CreatedAt,
    string Scope,
    string VirtualRoot,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

internal sealed record CreateWebDavTokenRequest(
    string? Username,
    string? Description,
    Guid? UserId = null,
    string? VirtualRoot = null,
    DateTimeOffset? ExpiresAt = null);

internal sealed record CreateWebDavTokenResponse(
    Guid Id,
    string Username,
    string Token,
    string? Description,
    DateTimeOffset CreatedAt,
    Guid UserId,
    string Scope,
    string VirtualRoot,
    DateTimeOffset ExpiresAt);
