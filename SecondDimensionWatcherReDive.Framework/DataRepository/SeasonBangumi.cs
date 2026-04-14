namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record SeasonBangumi(
    Guid Id,
    int MikanId,
    string Title,
    int DayOfWeek,
    string? ImageUrl,
    DateTimeOffset ScrapedAt);
