using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Utils.Scraper;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class SeasonController(
    ApplicationContext applicationContext,
    IHttpClientFactory httpClientFactory,
    IServiceProvider serviceProvider,
    ILogger<SeasonController> logger) : ControllerBase
{
    /// <summary>Get current season anime list (cached), or a specific season on-demand.</summary>
    [HttpGet]
    public async Task<ActionResult<SeasonResponse>> GetSeason(
        [FromQuery] int? year = null, [FromQuery] string? season = null)
    {
        // If year/season specified, scrape on-demand (not cached)
        if (year != null && !string.IsNullOrEmpty(season))
        {
            if (!MikananiScraper.SeasonMap.ContainsKey(season))
                return BadRequest(new { message = "Invalid season. Use: 春, 夏, 秋, 冬" });

            var httpClient = httpClientFactory.CreateClient("Feed");
            var scraped = await MikananiScraper.ScrapeSeasonAsync(
                httpClient, logger, year, season, HttpContext.RequestAborted);

            return Ok(new SeasonResponse
            {
                Year = year,
                Season = season,
                LastScrapedAt = DateTimeOffset.UtcNow,
                Bangumis = scraped.Select(b => new SeasonBangumiDto
                {
                    Id = Guid.Empty,
                    MikanId = b.MikanId,
                    Title = b.Title,
                    DayOfWeek = b.DayOfWeek,
                    ImageUrl = b.ImageUrl,
                    ScrapedAt = DateTimeOffset.UtcNow
                }).ToList()
            });
        }

        // Default: return cached current season
        var bangumis = await applicationContext.SeasonBangumis
            .AsNoTracking()
            .OrderBy(b => b.DayOfWeek)
            .ThenBy(b => b.Title)
            .Select(b => new SeasonBangumiDto
            {
                Id = b.Id,
                MikanId = b.MikanId,
                Title = b.Title,
                DayOfWeek = b.DayOfWeek,
                ImageUrl = b.ImageUrl,
                ScrapedAt = b.ScrapedAt
            })
            .ToListAsync();

        var lastScrapedAt = bangumis.Count > 0
            ? bangumis.Max(b => b.ScrapedAt)
            : (DateTimeOffset?)null;

        return Ok(new SeasonResponse { LastScrapedAt = lastScrapedAt, Bangumis = bangumis });
    }

    /// <summary>Get subgroups for a specific bangumi. Scrapes on-demand if stale.</summary>
    [HttpGet("{mikanId:int}/subgroups")]
    public async Task<ActionResult<List<SubgroupDto>>> GetSubgroups([FromRoute] int mikanId)
    {
        var bangumi = await applicationContext.SeasonBangumis
            .FirstOrDefaultAsync(b => b.MikanId == mikanId);

        if (bangumi == null)
            return NotFound();

        var cached = await applicationContext.BangumiSubgroups
            .Where(s => s.SeasonBangumiId == bangumi.Id)
            .ToListAsync();

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
                    existing.Name = entry.Name;
                    existing.ScrapedAt = now;
                }
                else
                {
                    applicationContext.BangumiSubgroups.Add(new BangumiSubgroup
                    {
                        Id = Guid.NewGuid(),
                        SeasonBangumiId = bangumi.Id,
                        MikanSubgroupId = entry.SubgroupId,
                        Name = entry.Name,
                        ScrapedAt = now
                    });
                }
            }

            await applicationContext.SaveChangesAsync();

            cached = await applicationContext.BangumiSubgroups
                .Where(s => s.SeasonBangumiId == bangumi.Id)
                .ToListAsync();
        }

        return Ok(cached.Select(s => new SubgroupDto
        {
            MikanSubgroupId = s.MikanSubgroupId,
            Name = s.Name,
            RssUrl = MikananiScraper.BuildRssUrl(mikanId, s.MikanSubgroupId)
        }).ToList());
    }

    /// <summary>Manually refresh the season anime list.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<SeasonResponse>> Refresh()
    {
        // Rate limit: reject if last scrape < 10 minutes ago
        var recent = await applicationContext.SeasonBangumis
            .OrderByDescending(b => b.ScrapedAt)
            .Select(b => b.ScrapedAt)
            .FirstOrDefaultAsync();

        if (recent != default && DateTimeOffset.UtcNow - recent < TimeSpan.FromMinutes(10))
            return StatusCode(429, new { message = "Please wait at least 10 minutes between refreshes" });

        var scraper = serviceProvider.GetServices<IHostedService>()
            .OfType<IScheduledTask>()
            .FirstOrDefault(t => t.Name == "ScrapeSeasonBangumi");

        if (scraper != null)
            await scraper.RunNowAsync(HttpContext.RequestAborted);

        return await GetSeason();
    }

    /// <summary>Subscribe to a bangumi by creating a Feed record.</summary>
    [HttpPost("subscribe")]
    public async Task<ActionResult<Feed>> Subscribe([FromBody] SubscribeRequest request)
    {
        var rssUrl = MikananiScraper.BuildRssUrl(request.MikanId, request.SubgroupId);

        // Check for duplicate
        var exists = await applicationContext.Feeds.AnyAsync(f => f.Url == rssUrl);
        if (exists) return Conflict(new { message = "Already subscribed" });

        // Look up bangumi title for the feed name
        var bangumi = await applicationContext.SeasonBangumis
            .FirstOrDefaultAsync(b => b.MikanId == request.MikanId);
        var feedName = bangumi?.Title ?? $"Bangumi {request.MikanId}";

        if (request.SubgroupId != null)
        {
            var subgroup = await applicationContext.BangumiSubgroups
                .FirstOrDefaultAsync(s =>
                    s.SeasonBangumiId == bangumi!.Id && s.MikanSubgroupId == request.SubgroupId);
            if (subgroup != null)
                feedName = $"{feedName} - {subgroup.Name}";
        }

        var feed = new Feed
        {
            Id = Guid.NewGuid(),
            Url = rssUrl,
            Name = feedName,
            CreatedAt = DateTimeOffset.Now
        };

        applicationContext.Feeds.Add(feed);
        await applicationContext.SaveChangesAsync();
        return Ok(feed);
    }

    public record SubscribeRequest(int MikanId, int? SubgroupId);

    public class SeasonResponse
    {
        public int? Year { get; set; }
        public string? Season { get; set; }
        public DateTimeOffset? LastScrapedAt { get; set; }
        public List<SeasonBangumiDto> Bangumis { get; set; } = [];
    }

    public class SeasonBangumiDto
    {
        public Guid Id { get; set; }
        public int MikanId { get; set; }
        public string Title { get; set; } = "";
        public int DayOfWeek { get; set; }
        public string? ImageUrl { get; set; }
        public DateTimeOffset ScrapedAt { get; set; }
    }

    public class SubgroupDto
    {
        public int MikanSubgroupId { get; set; }
        public string Name { get; set; } = "";
        public string RssUrl { get; set; } = "";
    }
}
