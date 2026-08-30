using System.Text.Json;
using Moq;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat.Tools;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ManageTasksToolTests
{
    [TestMethod]
    public async Task List_UsesSharedLeaseStatuses()
    {
        var task = new Mock<IScheduledTask>(MockBehavior.Strict);
        task.SetupGet(candidate => candidate.Id).Returns("remote-task");
        task.SetupGet(candidate => candidate.Interval).Returns(TimeSpan.FromMinutes(10));
        task.SetupGet(candidate => candidate.IsEnabled).Returns(true);
        var lastRunAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        var leaseManager = new Mock<IScheduledTaskLeaseManager>(MockBehavior.Strict);
        leaseManager.Setup(candidate => candidate.GetStatusesAsync(
                It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "remote-task" })),
                CancellationToken.None))
            .ReturnsAsync(new Dictionary<string, ScheduledTaskStatus>
            {
                ["remote-task"] = new(lastRunAt, true)
            });
        var tool = new ManageTasksTool([task.Object], leaseManager.Object);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new ManageTasksParams(ManageTasksAction.List),
                ToolJsonOptions.Options),
            CancellationToken.None);

        var success = result as ToolSuccessResult<TaskListResult>;
        Assert.IsNotNull(success);
        var status = success.Result.Tasks.Single();
        Assert.AreEqual(lastRunAt, status.LastRunAt);
        Assert.IsTrue(status.IsRunning);
    }
}
