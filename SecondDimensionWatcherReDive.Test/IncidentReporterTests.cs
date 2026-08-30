using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
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

    [TestMethod]
    public async Task ReportAsync_DiskSpaceLow_PublishesOnlySpecificEvent()
    {
        var incidentId = Guid.NewGuid();
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.UpsertAsync(
                It.IsAny<Incident>(),
                CancellationToken.None))
            .ReturnsAsync((Incident incident, CancellationToken _) =>
                incident with { Id = incidentId, Occurrence = 2 });
        var provider = new Mock<IServiceProvider>();
        provider.Setup(candidate => candidate.GetService(typeof(IIncidentRepository)))
            .Returns(repository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);
        var notifications = new Mock<INotificationPublisher>();
        var reporter = new IncidentReporter(
            factory.Object,
            Mock.Of<ILogger<IncidentReporter>>(),
            notifications.Object);

        await reporter.ReportAsync(new IncidentReport(
            IncidentType.DiskSpaceLow,
            IncidentSeverity.Critical,
            "Disk space is low",
            "Less than 5% is available.",
            "/downloads"), CancellationToken.None);

        notifications.Verify(candidate => candidate.PublishAsync(
            It.Is<NotificationEvent>(notification =>
                notification.Type == NotificationEventType.DiskSpaceLow
                && notification.DeduplicationKey == $"disk-space-low:{incidentId}:2"
                && notification.DeepLink == "/incidents?type=diskSpaceLow"),
            CancellationToken.None), Times.Once);
        notifications.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ReportAsync_RecurringIncident_DeduplicatesWithinEachOccurrence()
    {
        var incidentId = Guid.NewGuid();
        var occurrences = new Queue<int>([1, 2, 2]);
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.UpsertAsync(
                It.IsAny<Incident>(),
                CancellationToken.None))
            .ReturnsAsync((Incident incident, CancellationToken _) =>
                incident with { Id = incidentId, Occurrence = occurrences.Dequeue() });
        var provider = new Mock<IServiceProvider>();
        provider.Setup(candidate => candidate.GetService(typeof(IIncidentRepository)))
            .Returns(repository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);
        var published = new List<NotificationEvent>();
        var notifications = new Mock<INotificationPublisher>();
        notifications.Setup(candidate => candidate.PublishAsync(
                It.IsAny<NotificationEvent>(),
                CancellationToken.None))
            .Callback<NotificationEvent, CancellationToken>((notification, _) =>
                published.Add(notification))
            .Returns(Task.CompletedTask);
        var reporter = new IncidentReporter(
            factory.Object,
            Mock.Of<ILogger<IncidentReporter>>(),
            notifications.Object);
        var report = new IncidentReport(
            IncidentType.FeedFailure,
            IncidentSeverity.Error,
            "Feed failed",
            "The feed could not be loaded.",
            "https://example.test/feed");

        await reporter.ReportAsync(report, CancellationToken.None);
        await reporter.ReportAsync(report, CancellationToken.None);
        await reporter.ReportAsync(report, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                $"incident-opened:{incidentId}",
                $"incident-opened:{incidentId}:2",
                $"incident-opened:{incidentId}:2"
            },
            published.Select(notification => notification.DeduplicationKey).ToArray());
    }
}
