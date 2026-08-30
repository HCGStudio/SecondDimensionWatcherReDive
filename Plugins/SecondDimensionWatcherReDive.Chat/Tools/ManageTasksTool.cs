using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageTasksParams>(
    "manage_tasks",
    "Manage background scheduled tasks. List all task statuses or manually trigger a specific task to run.")]
internal sealed partial class ManageTasksTool(
    IEnumerable<IScheduledTask> scheduledTasks,
    IScheduledTaskLeaseManager leaseManager) : ITool
{
    private async Task<IToolResult> ExecuteCoreAsync(
        ManageTasksParams param, CancellationToken cancellationToken)
    {
        var taskList = scheduledTasks.ToList();

        IToolResult result;
        switch (param.Action)
        {
            case ManageTasksAction.List:
            {
                var statuses = await leaseManager.GetStatusesAsync(
                    taskList.Select(task => task.Id).ToArray(),
                    cancellationToken);
                result = new ToolSuccessResult<TaskListResult>(new TaskListResult(
                    taskList.Select(task =>
                    {
                        var status = statuses.GetValueOrDefault(task.Id)
                                     ?? new ScheduledTaskStatus(null, false);
                        return new TaskSummary(
                            task.Id,
                            task.Interval.ToString(),
                            task.IsEnabled,
                            status.LastRunAt,
                            status.IsRunning);
                    })));
                break;
            }

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

        return result;
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
