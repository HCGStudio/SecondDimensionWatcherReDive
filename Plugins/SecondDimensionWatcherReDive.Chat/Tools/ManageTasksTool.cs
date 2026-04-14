using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageTasksParams>(
    "manage_tasks",
    "Manage background scheduled tasks. List all task statuses or manually trigger a specific task to run.")]
internal sealed partial class ManageTasksTool(
    IEnumerable<IScheduledTask> scheduledTasks) : ITool
{
    private Task<IToolExecutionResult> ExecuteCoreAsync(
        ManageTasksParams param, CancellationToken cancellationToken)
    {
        var taskList = scheduledTasks.ToList();

        string result;
        switch (param.Action)
        {
            case ManageTasksAction.List:
                result = ChatToolHelper.Serialize(new TaskListResult(
                    taskList.Select(t => new TaskSummary(
                        t.Id, t.Interval.ToString(), t.IsEnabled, t.LastRunAt, t.IsRunning))));
                break;

            case ManageTasksAction.Run:
            {
                if (string.IsNullOrEmpty(param.TaskId))
                {
                    result = ChatToolHelper.Serialize(new ToolError("task_id is required"));
                    break;
                }

                var task = taskList.FirstOrDefault(t =>
                    string.Equals(t.Id, param.TaskId, StringComparison.OrdinalIgnoreCase));
                if (task is null)
                {
                    result = ChatToolHelper.Serialize(new ToolError($"Task '{param.TaskId}' not found"));
                    break;
                }

                task.Enqueue();
                result = ChatToolHelper.Serialize(new TaskRunResult(true, $"Task '{param.TaskId}' has been enqueued"));
                break;
            }

            default:
                result = ChatToolHelper.Serialize(new ToolError($"Unknown action: {param.Action}"));
                break;
        }

        return Task.FromResult<IToolExecutionResult>(new ToolStringResult(result));
    }
}
