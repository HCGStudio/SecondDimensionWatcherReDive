using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class IncidentsControllerTests
{
    [TestMethod]
    public async Task GetAsync_UnknownType_ReturnsBadRequestWithoutQueryingRepository()
    {
        var repository = new Mock<IIncidentRepository>(MockBehavior.Strict);
        var controller = new IncidentsController(
            repository.Object,
            Mock.Of<IIncidentRetryService>());

        var result = await controller.GetAsync(
            "not-a-real-type",
            0,
            50,
            false,
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        repository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task GetAsync_ReturnsCamelCaseTypesAndOpenSummary()
    {
        var incident = CreateIncident(IncidentType.FileMappingFailure);
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.GetPageAsync(
                IncidentType.FileMappingFailure,
                false,
                0,
                50,
                CancellationToken.None))
            .ReturnsAsync(new IncidentPage(
                [incident],
                1,
                3,
                new Dictionary<IncidentType, int>
                {
                    [IncidentType.FileMappingFailure] = 1,
                    [IncidentType.AiFailure] = 2
                }));
        var controller = new IncidentsController(
            repository.Object,
            Mock.Of<IIncidentRetryService>());

        var result = await controller.GetAsync(
            "fileMappingFailure",
            0,
            50,
            false,
            CancellationToken.None);

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result);
        var response = Assert.IsInstanceOfType<Controllers.External.IncidentListResponse>(ok.Value);
        Assert.AreEqual("fileMappingFailure", response.Items.Single().Type);
        Assert.AreEqual("error", response.Items.Single().Severity);
        Assert.AreEqual(3, response.OpenCount);
        Assert.AreEqual(2, response.CountsByType["aiFailure"]);
    }

    [TestMethod]
    public async Task RetryAsync_Success_ReturnsIncidentDirectly()
    {
        var incident = CreateIncident(IncidentType.FeedFailure) with
        {
            RetryCount = 1,
            LastRetryAt = DateTimeOffset.UtcNow
        };
        var retryService = new Mock<IIncidentRetryService>();
        retryService.Setup(service => service.RetryAsync(incident.Id, CancellationToken.None))
            .ReturnsAsync(new IncidentRetryResult(
                incident.Id,
                "queued",
                true,
                incident,
                null));
        var controller = new IncidentsController(
            Mock.Of<IIncidentRepository>(),
            retryService.Object);

        var result = await controller.RetryAsync(incident.Id, CancellationToken.None);

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result);
        var response = Assert.IsInstanceOfType<Controllers.External.IncidentItem>(ok.Value);
        Assert.AreEqual(incident.Id, response.Id);
        Assert.IsTrue(response.CanRetry);
    }

    private static Incident CreateIncident(IncidentType type)
    {
        var now = DateTimeOffset.UtcNow;
        return new Incident(
            Guid.NewGuid(),
            "fingerprint",
            type,
            IncidentSeverity.Error,
            "Failure",
            "Something failed",
            Guid.NewGuid().ToString(),
            now,
            now,
            null,
            0,
            null,
            null);
    }
}
