using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class TasksControllerTests
{
    [TestMethod]
    public async Task GetTasksAsync_UsesSharedLeaseStatuses()
    {
        var task = new Mock<IScheduledTask>(MockBehavior.Strict);
        task.SetupGet(candidate => candidate.Id).Returns("remote-task");
        task.SetupGet(candidate => candidate.Interval).Returns(TimeSpan.FromMinutes(10));
        task.SetupGet(candidate => candidate.IsEnabled).Returns(true);
        var lastRunAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var leaseManager = new Mock<IScheduledTaskLeaseManager>(MockBehavior.Strict);
        leaseManager.Setup(candidate => candidate.GetStatusesAsync(
                It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "remote-task" })),
                CancellationToken.None))
            .ReturnsAsync(new Dictionary<string, ScheduledTaskStatus>
            {
                ["remote-task"] = new(lastRunAt, true)
            });
        var controller = new TasksController([task.Object], leaseManager.Object);

        var result = await controller.GetTasksAsync(CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var response = ok.Value as IReadOnlyList<Controllers.External.ScheduledTask>;
        Assert.IsNotNull(response);
        Assert.HasCount(1, response);
        Assert.AreEqual(lastRunAt, response[0].LastRunAt);
        Assert.IsTrue(response[0].IsRunning);
    }
}
