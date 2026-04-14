using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.Scraper;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class SeasonController(
    ISeasonBangumiRepository seasonBangumiRepository,
    IBangumiSubgroupRepository bangumiSubgroupRepository,
    IFeedRepository feedRepository,
    IHttpClientFactory httpClientFactory,
    IEnumerable<IScheduledTask> scheduledTasks,
    ILogger<SeasonController> logger) : ControllerBase
{
    /// <summary>Get current season anime list (cached), or a specific season on-demand.</summary>
    [HttpGet]
    public async Task<IActionResult> GetSeason(
        [FromQuery] int? year = null, [FromQuery] string? season = null,
        CancellationToken cancellationToken = default)
    {
        // If year/season specified, scrape on-demand (not cached)
        if (year != null && !string.IsNullOrEmpty(season))
        {
            if (!MikananiScraper.SeasonMap.ContainsKey(season))
                return BadRequest(new { message = "Invalid season. Use: 春, 夏, 秋, 冬" });

            var httpClient = httpClientFactory.CreateClient("Feed");
            var scraped = await MikananiScraper.ScrapeSeasonAsync(
                httpClient, logger, year, season, cancellationToken);

            return Ok(new External.SeasonResponse(
                year,
                season,
                DateTimeOffset.UtcNow,
                scraped.Select(b => new External.SeasonBangumi(
                    Guid.Empty,
                    b.MikanId,
                    b.Title,
                    b.DayOfWeek,
                    b.ImageUrl,
                    DateTimeOffset.UtcNow)).ToList()));
        }

        // Default: return cached current season
        var bangumis = (await seasonBangumiRepository.GetAllOrderedByDayAndTitleAsync(cancellationToken))
            .Select(b => b.ToExternal())
            .ToList();

        var lastScrapedAt = bangumis.Count > 0
            ? bangumis.Max(b => b.ScrapedAt)
            : (DateTimeOffset?)null;

        return Ok(new External.SeasonResponse(null, null, lastScrapedAt, bangumis));
    }

    /// <summary>Get subgroups for a specific bangumi. Scrapes on-demand if stale.</summary>
    [HttpGet("{mikanId:int}/subgroups")]
    public async Task<IActionResult> GetSubgroups([FromRoute] int mikanId,
        CancellationToken cancellationToken)
    {
        var bangumi = await seasonBangumiRepository.FindByMikanIdAsync(mikanId, cancellationToken);

        if (bangumi == null)
            return NotFound();

        var cached = await bangumiSubgroupRepository.GetBySeasonBangumiIdAsync(bangumi.Id, cancellationToken);

        var staleThreshold = DateTimeOffset.UtcNow.AddHours(-24);
        if (cached.Count == 0 || cached.Any(s => s.ScrapedAt < staleThreshold))
        {
            // Scrape on-demand
            var httpClient = httpClientFactory.CreateClient("Feed");
            var scraped = await MikananiScraper.ScrapeSubgroupsAsync(
                httpClient, mikanId, logger);

            var now = DateTimeOffset.UtcNow;
            var existingBySubgroupId = cached.ToDictionary(s => s.MikanSubgroupId);

            foreach (var entry in scraped)
            {
                if (existingBySubgroupId.TryGetValue(entry.SubgroupId, out var existing))
                {
                    await bangumiSubgroupRepository.UpdateAsync(existing with { Name = entry.Name, ScrapedAt = now }, cancellationToken);
                }
                else
                {
                    await bangumiSubgroupRepository.AddAsync(new BangumiSubgroup(
                        Guid.NewGuid(), bangumi.Id, entry.SubgroupId, entry.Name, now), cancellationToken);
                }
            }

            cached = await bangumiSubgroupRepository.GetBySeasonBangumiIdAsync(bangumi.Id, cancellationToken);
        }

        return Ok(cached.Select(s => new External.Subgroup(
            s.MikanSubgroupId,
            s.Name,
            MikananiScraper.BuildRssUrl(mikanId, s.MikanSubgroupId))).ToList());
    }

    /// <summary>Manually refresh the season anime list.</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        // Rate limit: reject if last scrape < 10 minutes ago
        var recent = await seasonBangumiRepository.GetLatestScrapedAtAsync(cancellationToken);

        if (recent != null && DateTimeOffset.UtcNow - recent < TimeSpan.FromMinutes(10))
            return StatusCode(429, new { message = "Please wait at least 10 minutes between refreshes" });

        var scraper = scheduledTasks.FirstOrDefault(t => t.Id == "ScrapeSeasonBangumi");

        if (scraper != null)
            await scraper.RunNowAsync(cancellationToken);

        return await GetSeason(cancellationToken: cancellationToken);
    }

    /// <summary>Subscribe to a bangumi by creating a Feed record.</summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] External.SubscribeRequest request,
        CancellationToken cancellationToken)
    {
        var rssUrl = MikananiScraper.BuildRssUrl(request.MikanId, request.SubgroupId);

        // Check for duplicate
        var exists = await feedRepository.ExistsByUrlAsync(rssUrl, cancellationToken);
        if (exists) return Conflict(new { message = "Already subscribed" });

        // Look up bangumi title for the feed name
        var bangumi = await seasonBangumiRepository.FindByMikanIdAsync(request.MikanId, cancellationToken);
        var feedName = bangumi?.Title ?? $"Bangumi {request.MikanId}";

        if (request.SubgroupId != null)
        {
            var subgroup = await bangumiSubgroupRepository
                .FindBySeasonBangumiAndSubgroupIdAsync(bangumi!.Id, request.SubgroupId.Value, cancellationToken);
            if (subgroup != null)
                feedName = $"{feedName} - {subgroup.Name}";
        }

        var feed = new Feed(Guid.NewGuid(), rssUrl, feedName, DateTimeOffset.Now);

        await feedRepository.AddAsync(feed, cancellationToken);
        return Ok(feed.ToExternal());
    }
}
