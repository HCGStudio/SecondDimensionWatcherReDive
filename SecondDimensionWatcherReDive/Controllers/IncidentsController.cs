using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class IncidentsController(
    IIncidentRepository incidentRepository,
    IIncidentRetryService retryService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? type,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] bool includeResolved = false,
        CancellationToken cancellationToken = default,
        [FromQuery] Guid? focus = null)
    {
        if (skip < 0 || take is < 1 or > 200)
            return BadRequest(new { message = "skip must be non-negative and take must be between 1 and 200." });
        if (!TryParseType(type, out var parsedType))
            return BadRequest(new { message = $"Unknown incident type '{type}'." });

        var page = await incidentRepository.GetPageAsync(
            parsedType,
            includeResolved,
            skip,
            take,
            cancellationToken);
        var items = page.Items;
        if (focus.HasValue && items.All(item => item.Id != focus.Value))
        {
            var focused = await incidentRepository.FindByIdAsync(focus.Value, cancellationToken);
            if (focused is not null
                && (!parsedType.HasValue || focused.Type == parsedType.Value))
                items = [focused, .. items];
        }
        return Ok(new External.IncidentListResponse(
            items.Select(ToExternal).ToList(),
            page.TotalCount,
            page.OpenCount,
            page.OpenCountsByType.ToDictionary(
                pair => ToApiValue(pair.Key),
                pair => pair.Value,
                StringComparer.Ordinal)));
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> RetryAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await retryService.RetryAsync(id, cancellationToken);
        if (result is null) return NotFound();
        if (result.IsSuccess && result.Incident is not null)
            return Ok(ToExternal(result.Incident));

        var error = new External.IncidentRetryError(
            result.IncidentId,
            false,
            result.Error);
        return string.Equals(result.Status, "resolved", StringComparison.Ordinal)
            ? Conflict(error)
            : UnprocessableEntity(error);
    }

    [HttpPost("retry-all")]
    public async Task<IActionResult> RetryAllAsync(CancellationToken cancellationToken)
    {
        var result = await retryService.RetryAllAsync(cancellationToken);
        return Ok(new External.IncidentRetryBatchResponse(
            result.Attempted,
            result.Succeeded,
            result.Failed,
            result.Results.Select(item => new External.IncidentRetryError(
                    item.IncidentId,
                    item.IsSuccess,
                    item.Error))
                .ToList()));
    }

    private static External.IncidentItem ToExternal(Incident incident) => new(
        incident.Id,
        ToApiValue(incident.Type),
        incident.Severity.ToString().ToLowerInvariant(),
        incident.Title,
        incident.Detail,
        incident.SourceId,
        incident.DetectedAt,
        incident.UpdatedAt,
        incident.RetryCount,
        incident.LastRetryAt,
        incident.LastRetryError,
        incident.ResolvedAt,
        incident.ResolvedAt is null);

    private static bool TryParseType(string? value, out IncidentType? type)
    {
        type = value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "feedfailure" => IncidentType.FeedFailure,
            "downloadstalled" => IncidentType.DownloadStalled,
            "aifailure" => IncidentType.AiFailure,
            "filemappingfailure" => IncidentType.FileMappingFailure,
            "diskspacelow" => IncidentType.DiskSpaceLow,
            _ => (IncidentType?)(-1)
        };
        return type != (IncidentType?)(-1);
    }

    private static string ToApiValue(IncidentType type) => type switch
    {
        IncidentType.FeedFailure => "feedFailure",
        IncidentType.DownloadStalled => "downloadStalled",
        IncidentType.AiFailure => "aiFailure",
        IncidentType.FileMappingFailure => "fileMappingFailure",
        IncidentType.DiskSpaceLow => "diskSpaceLow",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
