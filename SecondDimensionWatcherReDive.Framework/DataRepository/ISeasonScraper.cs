namespace SecondDimensionWatcherReDive.Framework.DataRepository;

/// <summary>Scrapes season anime data from external sources (e.g. mikanani.me).</summary>
public interface ISeasonScraper
{
    /// <summary>Scrapes anime list for a specific year/season combination.</summary>
    Task<IReadOnlyList<SeasonBangumi>> ScrapeSeasonAsync(int year, AnimeSeason season, CancellationToken cancellationToken);
}
