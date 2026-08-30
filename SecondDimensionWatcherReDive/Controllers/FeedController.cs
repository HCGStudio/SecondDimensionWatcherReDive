using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class FeedController(IFeedRepository feedRepository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetFeeds(CancellationToken cancellationToken)
    {
        var feeds = await feedRepository.GetAllOrderedAsync(cancellationToken);
        return Ok(feeds.Select(f => f.ToExternal()).ToList());
    }

    [HttpPost]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> AddFeed([FromBody] External.AddFeedRequest request,
        CancellationToken cancellationToken)
    {
        var feed = new Feed(Guid.NewGuid(), request.Url, request.Name, DateTimeOffset.Now);

        await feedRepository.AddAsync(feed, cancellationToken);
        return Ok(feed.ToExternal());
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> RemoveFeed([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var feed = await feedRepository.FindByIdAsync(id, cancellationToken);
        if (feed is null) return NotFound();

        await feedRepository.RemoveAsync(feed, cancellationToken);
        return Ok();
    }
}
