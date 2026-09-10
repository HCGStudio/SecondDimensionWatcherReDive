using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class TasksController(
    IEnumerable<IScheduledTask> scheduledTasks,
    IScheduledTaskLeaseManager leaseManager) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTasksAsync(CancellationToken cancellationToken)
    {
        var taskList = scheduledTasks.ToList();
        var statuses = await leaseManager.GetStatusesAsync(
            taskList.Select(task => task.Id).ToArray(),
            cancellationToken);
        var tasks = taskList.Select(task =>
        {
            var status = statuses.GetValueOrDefault(task.Id)
                         ?? new ScheduledTaskStatus(null, false);
            return new External.ScheduledTask(
                task.Id,
                task.Interval.ToString(),
                task.IsEnabled,
                status.LastRunAt,
                status.IsRunning,
                task is IAISelectableTask);
        }).ToList();

        return Ok(tasks);
    }

    [HttpPost("{id}/run")]
    [Authorize(Policy = AccessPolicies.Administrator)]
    public IActionResult RunTask(
        [FromRoute] string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AIExecutionSelection? selection = null)
    {
        var task = scheduledTasks.FirstOrDefault(t =>
            string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        if (task == null)
            return NotFound(new { message = $"Task '{id}' not found" });

        if (task is IAISelectableTask selectableTask && selection is not null &&
            (!string.IsNullOrWhiteSpace(selection.ProviderId) ||
             !string.IsNullOrWhiteSpace(selection.Model) ||
             !string.IsNullOrWhiteSpace(selection.ReasoningEffort)))
        {
            try
            {
                HttpContext.RequestServices.GetRequiredService<IAISelectionValidator>()
                    .ValidateSelection(new ChatOptions
                    {
                        ProviderId = selection.ProviderId,
                        Model = selection.Model,
                        ReasoningEffort = selection.ReasoningEffort
                    }, requiresTools: true);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = exception.Message });
            }

            if (!selectableTask.TryEnqueue(selection))
                return Conflict(new { message = "The task already has a pending or running execution." });
        }
        else
        {
            task.Enqueue();
        }
        return Accepted(new { message = $"Task '{id}' enqueued" });
    }
}
