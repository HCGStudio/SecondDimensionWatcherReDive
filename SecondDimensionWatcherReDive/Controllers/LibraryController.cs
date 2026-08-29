using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/library")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class LibraryController(
    ILibrarySearchRepository searchRepository,
    IReleaseUpgradeRepository upgradeRepository,
    IReleaseUpgradeCoordinator upgradeCoordinator) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> SearchAsync(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int? season,
        [FromQuery] int? episode,
        [FromQuery] string? subtitleGroup,
        [FromQuery] string? resolution,
        [FromQuery] string? codec,
        [FromQuery] string? language,
        [FromQuery] string? downloadState,
        [FromQuery] string? watchState,
        [FromQuery(Name = "path")] string? virtualPath,
        [FromQuery] string? source,
        [FromQuery] string? sort,
        [FromQuery] string? cursor,
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (take is < 1 or > 100 || season is < 0 or > 100 || episode is < 0 or > 100000)
            return BadRequest(new { message = "Invalid pagination, season, or episode value." });
        if (!TryParse(downloadState, LibraryDownloadState.Any, out LibraryDownloadState parsedDownload) ||
            !TryParse(watchState, LibraryWatchState.Any, out LibraryWatchState parsedWatch) ||
            !TryParse(source, LibrarySourceKind.Any, out LibrarySourceKind parsedSource) ||
            !TryParse(sort, LibrarySearchSort.PublishedDescending, out LibrarySearchSort parsedSort))
            return BadRequest(new { message = "One or more search enum values are invalid." });

        try
        {
            var result = await searchRepository.SearchAsync(new LibrarySearchRequest(
                    Normalize(query),
                    season,
                    episode,
                    Normalize(subtitleGroup),
                    Normalize(resolution),
                    Normalize(codec),
                    Normalize(language),
                    parsedDownload,
                    parsedWatch,
                    Normalize(virtualPath),
                    parsedSource,
                    parsedSort,
                    Normalize(cursor),
                    take,
                    userId),
                cancellationToken);
            return Ok(result.ToExternal());
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("integrity")]
    public async Task<IActionResult> IntegrityAsync(
        [FromQuery] string? tmdbId,
        [FromQuery] int? season,
        CancellationToken cancellationToken)
    {
        if (season is < 0 or > 100) return BadRequest(new { message = "Invalid season." });
        var result = await searchRepository.GetIntegrityAsync(Normalize(tmdbId), season, cancellationToken);
        return Ok(result.Select(item => item.ToExternal()).ToList());
    }

    [HttpGet("upgrades")]
    public async Task<IActionResult> UpgradesAsync(
        [FromQuery] bool automaticOnly = false,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 200) return BadRequest(new { message = "take must be between 1 and 200." });
        var result = await upgradeRepository.GetCandidatesAsync(automaticOnly, take, cancellationToken);
        return Ok(result.Select(item => item.ToExternal()).ToList());
    }

    [HttpPost("upgrades/execute")]
    public async Task<IActionResult> ExecuteUpgradeAsync(
        [FromBody] External.ExecuteReleaseUpgradeRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = (await upgradeRepository.GetCandidatesAsync(false, 200, cancellationToken))
            .SingleOrDefault(item => item.CurrentReleaseId == request.CurrentReleaseId &&
                                     item.CandidateReleaseId == request.CandidateReleaseId);
        if (candidate is null)
            return Conflict(new { message = "The requested release is no longer an available upgrade." });

        var result = await upgradeCoordinator.ExecuteAsync(candidate, request.DryRun, cancellationToken);
        var response = result.ToExternal();
        return result.IsSuccess ? Ok(response) : UnprocessableEntity(response);
    }

    [HttpPost("upgrades/{operationId:guid}/rollback")]
    public async Task<IActionResult> RollbackUpgradeAsync(
        [FromRoute] Guid operationId,
        CancellationToken cancellationToken)
    {
        var result = await upgradeCoordinator.RollbackAsync(operationId, cancellationToken);
        var response = result.ToExternal();
        if (result.Outcome == "not_found") return NotFound(response);
        return result.IsSuccess ? Ok(response) : Conflict(response);
    }

    [HttpGet("upgrade-history")]
    public async Task<IActionResult> UpgradeHistoryAsync(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 200) return BadRequest(new { message = "take must be between 1 and 200." });
        var result = await upgradeRepository.GetHistoryAsync(take, cancellationToken);
        return Ok(result.Select(item => item.ToExternal()).ToList());
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = User.FindFirstValue("Id")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out userId);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParse<T>(string? value, T defaultValue, out T parsed)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = defaultValue;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }
}
