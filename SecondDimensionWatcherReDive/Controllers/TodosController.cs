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
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? focus = null,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > 200)
            return BadRequest(new
            {
                message = "skip must be non-negative and take must be between 1 and 200."
            });
        if (focus is not null && !IsValidKey(focus))
            return BadRequest(new { message = "focus must be a valid todo resource key." });

        var page = await todoRepository.GetAsync(
            includeRead,
            includeSnoozed,
            DateTimeOffset.UtcNow,
            skip,
            take,
            focus,
            cancellationToken);
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

    private static bool IsValidKey(string? key)
    {
        if (key is null || key.Length > 128) return false;

        var parts = key.Split(':');
        if (parts.Length == 2
            && (parts[0] is "automation" or "incident" or "metadata"))
            return Guid.TryParse(parts[1], out _);

        return parts.Length == 3
               && parts[0] == "incident"
               && Guid.TryParse(parts[1], out _)
               && int.TryParse(parts[2], out var occurrence)
               && occurrence > 1;
    }
}
