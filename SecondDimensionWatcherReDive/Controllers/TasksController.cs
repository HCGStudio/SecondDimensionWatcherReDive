using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal class TasksController(IEnumerable<IScheduledTask> scheduledTasks) : ControllerBase
{
    [HttpGet]
    public IActionResult GetTasks()
    {
        var tasks = scheduledTasks.Select(t => new External.ScheduledTask(
            t.Id,
            t.Interval.ToString(),
            t.IsEnabled,
            t.LastRunAt,
            t.IsRunning)).ToList();

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
