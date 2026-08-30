using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageTasksParams>(
    "manage_tasks",
    "Manage background scheduled tasks. List all task statuses or manually trigger a specific task to run.",
    ToolRiskLevel.Mutating)]
internal sealed partial class ManageTasksTool(
    IEnumerable<IScheduledTask> scheduledTasks) : ITool
{
    private Task<IToolResult> ExecuteCoreAsync(
        ManageTasksParams param, CancellationToken cancellationToken)
    {
        var taskList = scheduledTasks.ToList();

        IToolResult result;
        switch (param.Action)
        {
            case ManageTasksAction.List:
                result = new ToolSuccessResult<TaskListResult>(new TaskListResult(
                    taskList.Select(t => new TaskSummary(
                        t.Id, t.Interval.ToString(), t.IsEnabled, t.LastRunAt, t.IsRunning))));
                break;

            case ManageTasksAction.Run:
            {
                if (string.IsNullOrEmpty(param.TaskId))
                {
                    result = new ToolFailureResult("task_id is required");
                    break;
                }

                var task = taskList.FirstOrDefault(t =>
                    string.Equals(t.Id, param.TaskId, StringComparison.OrdinalIgnoreCase));
                if (task is null)
                {
                    result = new ToolFailureResult($"Task '{param.TaskId}' not found");
                    break;
                }

                task.Enqueue();
                result = new ToolSuccessResult<TaskRunResult>(
                    new TaskRunResult(true, $"Task '{param.TaskId}' has been enqueued"));
                break;
            }

            default:
                result = new ToolFailureResult($"Unknown action: {param.Action}");
                break;
        }

        return Task.FromResult(result);
    }
}

internal enum ManageTasksAction
{
    List,
    Run
}

internal sealed record ManageTasksParams(
    ManageTasksAction Action,
    string? TaskId = null);

internal sealed record TaskListResult(IEnumerable<TaskSummary> Tasks);
internal sealed record TaskSummary(string Id, string Interval, bool IsEnabled, DateTimeOffset? LastRunAt, bool IsRunning);
internal sealed record TaskRunResult(bool Success, string Message);
