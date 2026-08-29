using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class TodosControllerTests
{
    [TestMethod]
    public async Task UpdateStateAsync_MarkRead_OnlyPersistsPresentationState()
    {
        var id = Guid.NewGuid();
        var repository = new Mock<ITodoRepository>();
        var controller = new TodosController(repository.Object);

        var result = await controller.UpdateStateAsync(
            new UpdateTodoStateRequest([$"automation:{id}"], TodoStateAction.MarkRead, null),
            CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        repository.Verify(candidate => candidate.SetStateAsync(
            It.Is<IReadOnlyCollection<string>>(keys => keys.Single() == $"automation:{id}"),
            It.Is<DateTimeOffset?>(value => value.HasValue),
            true,
            null,
            false,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task UpdateStateAsync_SnoozeWithoutFutureTime_IsRejected()
    {
        var repository = new Mock<ITodoRepository>();
        var controller = new TodosController(repository.Object);

        var result = await controller.UpdateStateAsync(
            new UpdateTodoStateRequest(
                [$"incident:{Guid.NewGuid()}"],
                TodoStateAction.Snooze,
                DateTimeOffset.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        Assert.IsInstanceOfType<ObjectResult>(result);
        repository.VerifyNoOtherCalls();
    }
}
