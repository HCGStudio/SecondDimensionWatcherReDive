namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record Feed(Guid Id, string Url, string? Name, DateTimeOffset CreatedAt);
