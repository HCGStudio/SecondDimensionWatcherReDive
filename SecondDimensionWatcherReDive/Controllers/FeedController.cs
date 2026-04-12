using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Models;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class FeedController(ApplicationContext applicationContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<Feed>>> GetFeeds()
    {
        var feeds = await applicationContext.Feeds.AsNoTracking().OrderByDescending(f => f.CreatedAt).ToListAsync();
        return Ok(feeds);
    }

    [HttpPost]
    public async Task<ActionResult<Feed>> AddFeed([FromBody] AddFeedRequest request)
    {
        var feed = new Feed
        {
            Id = Guid.NewGuid(),
            Url = request.Url,
            Name = request.Name,
            CreatedAt = DateTimeOffset.Now
        };

        applicationContext.Feeds.Add(feed);
        await applicationContext.SaveChangesAsync();
        return Ok(feed);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveFeed([FromRoute] Guid id)
    {
        var feed = await applicationContext.Feeds.FindAsync(id);
        if (feed is null) return NotFound();

        applicationContext.Feeds.Remove(feed);
        await applicationContext.SaveChangesAsync();
        return Ok();
    }

    public record AddFeedRequest(string Url, string? Name);
}
