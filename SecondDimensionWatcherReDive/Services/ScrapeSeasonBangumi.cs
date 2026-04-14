using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.Scraper;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     Scheduled task that scrapes the mikanani.me homepage for current season anime
///     and caches the results in the database.
/// </summary>
public partial class ScrapeSeasonBangumi(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<ScrapeSeasonBangumi> logger)
    : ScheduledTaskBase
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");

    public override string Id => "ScrapeSeasonBangumi";
    public override TimeSpan Interval => TimeSpan.FromDays(7);

    protected override Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        return ScrapeHomepage(cancellationToken);
    }

    private async Task ScrapeHomepage(CancellationToken cancellationToken)
    {
        try
        {
            var scraped = await MikananiScraper.ScrapeSeasonAsync(_httpClient, logger, cancellationToken: cancellationToken);
            if (scraped.Count == 0)
            {
                LogScrapedZeroBangumi(logger);
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var seasonBangumiRepository = scope.ServiceProvider.GetRequiredService<ISeasonBangumiRepository>();

            var existing = await seasonBangumiRepository.GetAllAsync(cancellationToken);
            var existingByMikanId = existing.ToDictionary(b => b.MikanId);
            var scrapedIds = new HashSet<int>();
            var now = DateTimeOffset.UtcNow;

            foreach (var entry in scraped)
            {
                scrapedIds.Add(entry.MikanId);

                if (existingByMikanId.TryGetValue(entry.MikanId, out var bangumi))
                {
                    await seasonBangumiRepository.UpdateAsync(bangumi with
                    {
                        Title = entry.Title,
                        DayOfWeek = entry.DayOfWeek,
                        ImageUrl = entry.ImageUrl,
                        ScrapedAt = now
                    }, cancellationToken);
                }
                else
                {
                    await seasonBangumiRepository.AddAsync(new SeasonBangumi(
                        Guid.NewGuid(), entry.MikanId, entry.Title, entry.DayOfWeek,
                        entry.ImageUrl, now), cancellationToken);
                }
            }

            // Remove stale entries no longer on homepage
            var stale = existing.Where(b => !scrapedIds.Contains(b.MikanId)).ToList();
            if (stale.Count > 0)
            {
                await seasonBangumiRepository.RemoveRangeAsync(stale, cancellationToken);
                LogRemovedStaleBangumi(logger, stale.Count);
            }

            LogSeasonBangumiUpdated(logger, scraped.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogScrapeSeasonBangumiFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scraped 0 bangumi entries, skipping DB update")]
    private static partial void LogScrapedZeroBangumi(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} stale season bangumi entries")]
    private static partial void LogRemovedStaleBangumi(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Season bangumi cache updated: {Count} entries")]
    private static partial void LogSeasonBangumiUpdated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to scrape season bangumi from mikanani.me")]
    private static partial void LogScrapeSeasonBangumiFailed(ILogger logger, Exception ex);
}
