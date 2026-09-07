using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.LibraryCompletion;
namespace SecondDimensionWatcherReDive.Controllers;

[ApiController, Route("api/library/completion")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class LibraryCompletionController(LibraryCompletionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> PlanAsync(string tmdbId, int season, CancellationToken cancellationToken)
    {
        if (!int.TryParse(tmdbId, out var id) || id <= 0 || season is < 1 or > 100) return BadRequest();
        return Ok(await service.GetPlanAsync(tmdbId, season, cancellationToken));
    }
    [HttpPost, Authorize(Policy = AccessPolicies.ContentWrite)]
    public async Task<IActionResult> SubmitAsync(CompletionSubmissionRequest request, CancellationToken cancellationToken)
    {
        if (!int.TryParse(request.TmdbId, out var id) || id <= 0 || request.Season is < 1 or > 100 ||
            request.Selections is not { Count: > 0 and <= 100 } || request.Selections.Any(x => x.Episode <= 0 || x.ReleaseId == Guid.Empty) ||
            request.Selections.Select(x => x.Episode).Distinct().Count() != request.Selections.Count) return BadRequest();
        return Ok(await service.SubmitAsync(request, cancellationToken));
    }
}
