using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class TasksController(IServiceProvider serviceProvider) : ControllerBase
{
    private List<IScheduledTask> GetScheduledTasks()
    {
        return serviceProvider.GetServices<IHostedService>()
            .OfType<IScheduledTask>()
            .ToList();
    }

    [HttpGet]
    public ActionResult<List<TaskDto>> GetTasks()
    {
        var tasks = GetScheduledTasks().Select(t => new TaskDto
        {
            Name = t.Name,
            Description = t.Description,
            Interval = t.Interval.ToString(),
            IsEnabled = t.IsEnabled,
            LastRunAt = t.LastRunAt,
            IsRunning = t.IsRunning
        }).ToList();

        return Ok(tasks);
    }

    [HttpPost("{name}/run")]
    public async Task<IActionResult> RunTask([FromRoute] string name)
    {
        var task = GetScheduledTasks().FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        if (task == null)
            return NotFound(new { message = $"Task '{name}' not found" });

        if (task.IsRunning)
            return Conflict(new { message = $"Task '{name}' is already running" });

        await task.RunNowAsync(HttpContext.RequestAborted);
        return Ok(new { message = $"Task '{name}' completed" });
    }

    public class TaskDto
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Interval { get; set; } = "";
        public bool IsEnabled { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        public bool IsRunning { get; set; }
    }
}
