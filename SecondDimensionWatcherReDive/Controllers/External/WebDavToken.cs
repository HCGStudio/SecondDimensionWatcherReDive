namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record WebDavTokenSummary(
    Guid Id,
    string Username,
    string? Description,
    DateTimeOffset CreatedAt);

internal sealed record CreateWebDavTokenRequest(string? Username, string? Description);

internal sealed record CreateWebDavTokenResponse(
    Guid Id,
    string Username,
    string Token,
    string? Description,
    DateTimeOffset CreatedAt);
