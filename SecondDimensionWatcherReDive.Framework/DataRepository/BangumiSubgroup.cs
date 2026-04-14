namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record BangumiSubgroup(
    Guid Id,
    Guid SeasonBangumiId,
    int MikanSubgroupId,
    string Name,
    DateTimeOffset ScrapedAt);
