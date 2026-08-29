using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/todos")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class TodosController(ITodoRepository todoRepository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TodoListResponse>> GetAsync(
        [FromQuery] bool includeRead = false,
        [FromQuery] bool includeSnoozed = false,
        CancellationToken cancellationToken = default)
    {
        var page = await todoRepository.GetAsync(
            includeRead, includeSnoozed, DateTimeOffset.UtcNow, cancellationToken);
        return Ok(new TodoListResponse(
            page.Items.Select(item => new TodoItemResponse(
                item.Key,
                item.Type.ToString(),
                item.Priority.ToString(),
                item.Title,
                item.Detail,
                item.DeepLink,
                item.ResourceId,
                item.OccurredAt,
                item.ReadAt,
                item.SnoozedUntil)).ToList(),
            page.TotalCount,
            page.UnreadCount));
    }

    [HttpPatch("state")]
    public async Task<IActionResult> UpdateStateAsync(
        [FromBody] UpdateTodoStateRequest request,
        CancellationToken cancellationToken)
    {
        var keys = request.Keys
            .Where(IsValidKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length != request.Keys.Count)
        {
            ModelState.AddModelError("keys", "Todo keys must be unique valid resource keys.");
            return ValidationProblem(ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? readAt = null;
        DateTimeOffset? snoozedUntil = null;
        var updateRead = false;
        var updateSnooze = false;
        switch (request.Action)
        {
            case TodoStateAction.MarkRead:
                updateRead = true;
                readAt = now;
                break;
            case TodoStateAction.MarkUnread:
                updateRead = true;
                break;
            case TodoStateAction.Snooze:
                if (request.SnoozedUntil is null || request.SnoozedUntil <= now)
                {
                    ModelState.AddModelError("snoozedUntil", "A future time is required when snoozing.");
                    return ValidationProblem(ModelState);
                }
                updateSnooze = true;
                snoozedUntil = request.SnoozedUntil;
                break;
            case TodoStateAction.Unsnooze:
                updateSnooze = true;
                break;
            default:
                return BadRequest();
        }

        await todoRepository.SetStateAsync(
            keys, readAt, updateRead, snoozedUntil, updateSnooze, cancellationToken);
        return NoContent();
    }

    private static bool IsValidKey(string? key) =>
        key is not null
        && key.Length <= 128
        && (key.StartsWith("automation:", StringComparison.Ordinal)
            || key.StartsWith("incident:", StringComparison.Ordinal)
            || key.StartsWith("metadata:", StringComparison.Ordinal))
        && Guid.TryParse(key[(key.IndexOf(':') + 1)..], out _);
}
