using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/watchlist")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class WatchlistController(IWatchlistRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTimeOffset weekStart, CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var profileId)) return Unauthorized();
        if (weekStart == default) weekStart = DateTimeOffset.UtcNow.Date;
        return Ok(await repository.GetAsync(profileId, weekStart, weekStart.AddDays(7), cancellationToken));
    }

    [HttpPut]
    [Authorize(Policy = AccessPolicies.PlaybackWrite)]
    public async Task<IActionResult> Save([FromBody] External.WatchlistRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var profileId)) return Unauthorized();
        if (!Statuses.Contains(request.Status) || (request.Id is null && request.TmdbId is null && request.MikanId is null)
            || request.MikanId <= 0 || string.IsNullOrWhiteSpace(request.Title)) return BadRequest();
        string? tmdbId = null;
        if (request.TmdbId is not null)
        {
            if (!long.TryParse(request.TmdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdb) || tmdb <= 0)
                return BadRequest();
            tmdbId = tmdb.ToString(CultureInfo.InvariantCulture);
        }
        try
        {
            await repository.UpsertAsync(profileId,
                new WatchlistUpdate(request.Id, tmdbId, request.MikanId, request.Title, request.Status,
                    request.TmdbIdSpecified, request.MikanIdSpecified), cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException) { return Conflict(); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AccessPolicies.PlaybackWrite)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var profileId)) return Unauthorized();
        return await repository.DeleteAsync(profileId, id, cancellationToken) ? NoContent() : NotFound();
    }

    private static readonly HashSet<string> Statuses = ["planned", "watching", "onHold", "dropped", "completed"];
}
