using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.MigrationTasks;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/migrations")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class MigrationsController(
    IMigrationStateRepository stateRepository,
    MigrationAdministrationService administrationService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
    {
        var states = await stateRepository.GetAllAsync(cancellationToken);
        return Ok(states.Select(ToExternal).ToList());
    }

    [HttpPost("{key}/{version:int}/retry")]
    public async Task<IActionResult> RetryAsync(
        [FromRoute] string key,
        [FromRoute] int version,
        CancellationToken cancellationToken)
    {
        var result = await administrationService.RetryAsync(
            key,
            version,
            cancellationToken);
        var response = new External.MigrationRetryResponse(
            result.Status == MigrationRetryStatus.Completed,
            result.Execution is null ? null : ToExternal(result.Execution),
            result.Error);
        return result.Status switch
        {
            MigrationRetryStatus.Completed => Ok(response),
            MigrationRetryStatus.NotFound => NotFound(response),
            MigrationRetryStatus.NotFailed => Conflict(response),
            MigrationRetryStatus.Failed => UnprocessableEntity(response),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static External.MigrationExecutionResponse ToExternal(
        MigrationExecution execution) => new(
        execution.Key,
        execution.Version,
        execution.Status.ToString().ToLowerInvariant(),
        execution.Checkpoint,
        execution.StartedAt,
        execution.FinishedAt,
        execution.UpdatedAt,
        execution.AttemptCount,
        execution.LastErrorSummary);
}
