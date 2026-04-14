namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record AddFeedRequest(string Url, string? Name);

internal sealed record Feed(Guid Id, string Url, string? Name, DateTimeOffset CreatedAt);
