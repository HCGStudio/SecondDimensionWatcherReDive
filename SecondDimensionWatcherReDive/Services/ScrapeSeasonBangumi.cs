using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.Scraper;

namespace SecondDimensionWatcherReDive.Services;

/// <summary>
///     Background service that scrapes the mikanani.me homepage for current season anime
///     and caches the results in the database. Runs on startup, then every 7 days.
/// </summary>
public class ScrapeSeasonBangumi(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<ScrapeSeasonBangumi> logger)
    : BackgroundService, IScheduledTask
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("Feed");
    private volatile bool _isRunning;
    private DateTimeOffset? _lastRunAt;

    public string Name => "ScrapeSeasonBangumi";
    public string Description => "更新当季番组列表";
    public TimeSpan Interval => TimeSpan.FromDays(7);
    public bool IsEnabled => true;
    public DateTimeOffset? LastRunAt => _lastRunAt;
    public bool IsRunning => _isRunning;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await RunNowAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, cancellationToken);
            await RunNowAsync(cancellationToken);
        }
    }

    public async Task RunNowAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        try
        {
            await ScrapeHomepage(cancellationToken);
            _lastRunAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _isRunning = false;
        }
    }

    public async Task ScrapeHomepage(CancellationToken cancellationToken)
    {
        try
        {
            var scraped = await MikananiScraper.ScrapeSeasonAsync(_httpClient, logger, cancellationToken: cancellationToken);
            if (scraped.Count == 0)
            {
                logger.LogWarning("Scraped 0 bangumi entries, skipping DB update");
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            await using var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();

            var existing = await context.SeasonBangumis.ToListAsync(cancellationToken);
            var existingByMikanId = existing.ToDictionary(b => b.MikanId);
            var scrapedIds = new HashSet<int>();
            var now = DateTimeOffset.UtcNow;

            foreach (var entry in scraped)
            {
                scrapedIds.Add(entry.MikanId);

                if (existingByMikanId.TryGetValue(entry.MikanId, out var bangumi))
                {
                    bangumi.Title = entry.Title;
                    bangumi.DayOfWeek = entry.DayOfWeek;
                    bangumi.ImageUrl = entry.ImageUrl;
                    bangumi.ScrapedAt = now;
                }
                else
                {
                    context.SeasonBangumis.Add(new SeasonBangumi
                    {
                        Id = Guid.NewGuid(),
                        MikanId = entry.MikanId,
                        Title = entry.Title,
                        DayOfWeek = entry.DayOfWeek,
                        ImageUrl = entry.ImageUrl,
                        ScrapedAt = now
                    });
                }
            }

            // Remove stale entries no longer on homepage
            var stale = existing.Where(b => !scrapedIds.Contains(b.MikanId)).ToList();
            if (stale.Count > 0)
            {
                context.SeasonBangumis.RemoveRange(stale);
                logger.LogInformation("Removed {Count} stale season bangumi entries", stale.Count);
            }

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Season bangumi cache updated: {Count} entries", scraped.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to scrape season bangumi from mikanani.me");
        }
    }
}
