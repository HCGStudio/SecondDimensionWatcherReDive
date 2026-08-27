using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class IncidentReporterTests
{
    [TestMethod]
    public async Task ReportAsync_SameTypeAndSource_UsesStableFingerprint()
    {
        var persisted = new List<Incident>();
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.UpsertAsync(
                It.IsAny<Incident>(),
                CancellationToken.None))
            .Callback<Incident, CancellationToken>((incident, _) => persisted.Add(incident))
            .ReturnsAsync((Incident incident, CancellationToken _) => incident);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(candidate => candidate.GetService(typeof(IIncidentRepository)))
            .Returns(repository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);
        var reporter = new IncidentReporter(
            factory.Object,
            Mock.Of<ILogger<IncidentReporter>>());

        await reporter.ReportAsync(new IncidentReport(
            IncidentType.FeedFailure,
            IncidentSeverity.Error,
            "First title",
            "First detail",
            "https://example.test/feed"), CancellationToken.None);
        await reporter.ReportAsync(new IncidentReport(
            IncidentType.FeedFailure,
            IncidentSeverity.Critical,
            "Changed title",
            "Changed detail",
            "https://example.test/feed"), CancellationToken.None);

        Assert.AreEqual(2, persisted.Count);
        Assert.AreEqual(persisted[0].Fingerprint, persisted[1].Fingerprint);
        Assert.AreNotEqual(persisted[0].Id, persisted[1].Id);
    }
}
