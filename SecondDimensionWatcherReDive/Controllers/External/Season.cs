namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record SeasonResponse(
    int? Year,
    string? Season,
    DateTimeOffset? LastScrapedAt,
    IReadOnlyList<SeasonBangumi> Bangumis);

internal sealed record SeasonBangumi(
    Guid Id,
    int MikanId,
    string Title,
    int DayOfWeek,
    string? ImageUrl,
    DateTimeOffset ScrapedAt);

internal sealed record Subgroup(
    int MikanSubgroupId,
    string Name,
    string RssUrl);

internal sealed record SubscribeRequest(int MikanId, int? SubgroupId);
