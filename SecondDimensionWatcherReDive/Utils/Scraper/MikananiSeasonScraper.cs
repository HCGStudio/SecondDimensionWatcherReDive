using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.Scraper;

internal sealed class MikananiSeasonScraper(
    IHttpClientFactory httpClientFactory,
    ILogger<MikananiSeasonScraper> logger) : ISeasonScraper
{
    public async Task<IReadOnlyList<SeasonBangumi>> ScrapeSeasonAsync(
        int year, AnimeSeason season, CancellationToken cancellationToken)
    {
        var seasonStr = season switch
        {
            AnimeSeason.Spring => "春",
            AnimeSeason.Summer => "夏",
            AnimeSeason.Autumn => "秋",
            AnimeSeason.Winter => "冬",
            _ => throw new ArgumentOutOfRangeException(nameof(season))
        };

        var httpClient = httpClientFactory.CreateClient("Feed");
        var scraped = await MikananiScraper.ScrapeSeasonAsync(
            httpClient, logger, year, seasonStr, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        return scraped
            .Select(b => new SeasonBangumi(Guid.Empty, b.MikanId, b.Title, b.DayOfWeek, b.ImageUrl, now))
            .ToList();
    }
}
