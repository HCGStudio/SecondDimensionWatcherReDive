using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class DurableJobsControllerTests
{
    [TestMethod]
    public async Task GetAsync_MapsDeadLettersWithoutPayloadOrLeaseData()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new DurableJob(
            Guid.NewGuid(),
            "secret-deduplication-key",
            DurableJobType.DownloadCompletion,
            DurableJobStatus.DeadLetter,
            DurableJobStage.Notify,
            "{\"storePath\":\"/secret/path\"}",
            8,
            now,
            now,
            now,
            now,
            null,
            "private-host:worker",
            now,
            "InvalidOperationException");
        var repository = new Mock<IDurableJobRepository>();
        repository.Setup(candidate => candidate.GetPageAsync(
                DurableJobStatus.DeadLetter,
                0,
                50,
                CancellationToken.None))
            .ReturnsAsync(new DurableJobPage([job], 1));
        var controller = new DurableJobsController(repository.Object);

        var result = await controller.GetAsync(
            "deadLetter", 0, 50, CancellationToken.None);

        var response = (DurableJobListResponse)((OkObjectResult)result).Value!;
        Assert.HasCount(1, response.Items);
        Assert.AreEqual(job.Id, response.Items[0].Id);
        Assert.AreEqual("notify", response.Items[0].Stage);
        Assert.IsNull(typeof(DurableJobItem).GetProperty("PayloadJson"));
        Assert.IsNull(typeof(DurableJobItem).GetProperty("LeaseOwner"));
        Assert.IsNull(typeof(DurableJobItem).GetProperty("DeduplicationKey"));
    }

    [TestMethod]
    public async Task RetryAsync_DeduplicatesIds()
    {
        var id = Guid.NewGuid();
        var repository = new Mock<IDurableJobRepository>();
        repository.Setup(candidate => candidate.RetryAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(id)),
                It.IsAny<DateTimeOffset>(),
                CancellationToken.None))
            .ReturnsAsync(1);
        var controller = new DurableJobsController(repository.Object);

        var result = await controller.RetryAsync(
            new DurableJobMutationRequest([id, id]),
            CancellationToken.None);

        var response = (DurableJobMutationResponse)((OkObjectResult)result).Value!;
        Assert.AreEqual(1, response.AffectedCount);
    }
}
