using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class TasksController(IEnumerable<IScheduledTask> scheduledTasks) : ControllerBase
{
    [HttpGet]
    public ActionResult<List<TaskDto>> GetTasks()
    {
        var tasks = scheduledTasks.Select(t => new TaskDto
        {
            Id = t.Id,
            Interval = t.Interval.ToString(),
            IsEnabled = t.IsEnabled,
            LastRunAt = t.LastRunAt,
            IsRunning = t.IsRunning
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

    public class TaskDto
    {
        public string Id { get; set; } = "";
        public string Interval { get; set; } = "";
        public bool IsEnabled { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        public bool IsRunning { get; set; }
    }
}
