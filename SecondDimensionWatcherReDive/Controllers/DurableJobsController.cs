using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/jobs")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class DurableJobsController(IDurableJobRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 200)
            return BadRequest(new
            {
                message = "skip must be non-negative and take must be between 1 and 200."
            });
        if (!TryParseStatus(status, out var parsedStatus))
            return BadRequest(new { message = $"Unknown job status '{status}'." });

        var page = await repository.GetPageAsync(
            parsedStatus,
            skip,
            take,
            cancellationToken);
        return Ok(new External.DurableJobListResponse(
            page.Items.Select(ToExternal).ToList(),
            page.TotalCount));
    }

    [HttpPost("retry")]
    public async Task<IActionResult> RetryAsync(
        [FromBody] External.DurableJobMutationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Ids is null || request.Ids.Count is < 1 or > 200)
            return BadRequest(new { message = "ids must contain between 1 and 200 jobs." });

        var affected = await repository.RetryAsync(
            request.Ids.Distinct().ToList(),
            DateTimeOffset.UtcNow,
            cancellationToken);
        return Ok(new External.DurableJobMutationResponse(affected));
    }

    [HttpPost("resolve")]
    public async Task<IActionResult> ResolveAsync(
        [FromBody] External.DurableJobMutationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Ids is null || request.Ids.Count is < 1 or > 200)
            return BadRequest(new { message = "ids must contain between 1 and 200 jobs." });

        var affected = await repository.ResolveAsync(
            request.Ids.Distinct().ToList(),
            DateTimeOffset.UtcNow,
            cancellationToken);
        return Ok(new External.DurableJobMutationResponse(affected));
    }

    private static External.DurableJobItem ToExternal(DurableJob job) => new(
        job.Id,
        ToApiValue(job.Type),
        ToApiValue(job.Status),
        ToApiValue(job.Stage),
        job.AttemptCount,
        job.CreatedAt,
        job.UpdatedAt,
        job.NextAttemptAt,
        job.LastAttemptAt,
        job.CompletedAt,
        job.LastError);

    private static bool TryParseStatus(string? value, out DurableJobStatus? status)
    {
        status = value?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "pending" => DurableJobStatus.Pending,
            "processing" => DurableJobStatus.Processing,
            "completed" => DurableJobStatus.Completed,
            "deadletter" or "dead-letter" => DurableJobStatus.DeadLetter,
            "resolved" => DurableJobStatus.Resolved,
            _ => (DurableJobStatus?)(-1)
        };
        return status != (DurableJobStatus?)(-1);
    }

    private static string ToApiValue(DurableJobType type) => type switch
    {
        DurableJobType.DownloadCompletion => "downloadCompletion",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static string ToApiValue(DurableJobStatus status) => status switch
    {
        DurableJobStatus.Pending => "pending",
        DurableJobStatus.Processing => "processing",
        DurableJobStatus.Completed => "completed",
        DurableJobStatus.DeadLetter => "deadLetter",
        DurableJobStatus.Resolved => "resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static string ToApiValue(DurableJobStage stage) => stage switch
    {
        DurableJobStage.MapFiles => "mapFiles",
        DurableJobStage.Notify => "notify",
        DurableJobStage.InvokePlugins => "invokePlugins",
        DurableJobStage.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };
}
