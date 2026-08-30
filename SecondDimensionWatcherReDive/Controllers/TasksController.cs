using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
                status.IsRunning);
        }).ToList();

        return Ok(tasks);
    }

    [HttpPost("{id}/run")]
    public IActionResult RunTask([FromRoute] string id)
    {
        var task = scheduledTasks.FirstOrDefault(t =>
            string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

        if (task == null)
            return NotFound(new { message = $"Task '{id}' not found" });

        task.Enqueue();
        return Accepted(new { message = $"Task '{id}' enqueued" });
    }
}
