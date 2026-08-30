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
    public async Task GetAsync_ForwardsDatabasePagination()
    {
        var repository = new Mock<ITodoRepository>();
        repository.Setup(candidate => candidate.GetAsync(
                true,
                true,
                It.IsAny<DateTimeOffset>(),
                25,
                10,
                CancellationToken.None))
            .ReturnsAsync(new TodoPage([], 120, 7));
        var controller = new TodosController(repository.Object);

        var result = await controller.GetAsync(
            true,
            true,
            25,
            10,
            CancellationToken.None);

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        var response = Assert.IsInstanceOfType<TodoListResponse>(ok.Value);
        Assert.AreEqual(120, response.TotalCount);
        Assert.AreEqual(7, response.UnreadCount);
    }

    [TestMethod]
    public async Task GetAsync_InvalidPagination_IsRejectedBeforeRepositoryQuery()
    {
        var repository = new Mock<ITodoRepository>();
        var controller = new TodosController(repository.Object);

        var result = await controller.GetAsync(
            false,
            false,
            0,
            201,
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result.Result);
        repository.VerifyNoOtherCalls();
    }

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

    [TestMethod]
    public async Task UpdateStateAsync_RecurringIncidentKey_IsAccepted()
    {
        var key = $"incident:{Guid.NewGuid()}:2";
        var repository = new Mock<ITodoRepository>();
        var controller = new TodosController(repository.Object);

        var result = await controller.UpdateStateAsync(
            new UpdateTodoStateRequest([key], TodoStateAction.Unsnooze, null),
            CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        repository.Verify(candidate => candidate.SetStateAsync(
            It.Is<IReadOnlyCollection<string>>(keys => keys.Single() == key),
            null,
            false,
            null,
            true,
            CancellationToken.None), Times.Once);
    }
}
