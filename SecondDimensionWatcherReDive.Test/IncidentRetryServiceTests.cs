using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class IncidentRetryServiceTests
{
    [TestMethod]
    public async Task RetryAsync_FeedFailure_EnqueuesWithoutWaitingAndKeepsIncidentOpen()
    {
        var incident = CreateIncident(IncidentType.FeedFailure, "https://example.test/feed.xml");
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.FindByIdAsync(incident.Id, CancellationToken.None))
            .ReturnsAsync(incident);
        repository.Setup(candidate => candidate.RecordRetryAsync(
                incident.Id,
                It.IsAny<DateTimeOffset>(),
                null,
                false,
                CancellationToken.None))
            .ReturnsAsync(incident with { RetryCount = 1, LastRetryAt = DateTimeOffset.UtcNow });
        var scheduledTask = new Mock<IScheduledTask>();
        scheduledTask.SetupGet(task => task.Id).Returns("SyncFeed");
        var service = CreateService(repository.Object, [scheduledTask.Object]);

        var result = await service.RetryAsync(incident.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("queued", result.Status);
        scheduledTask.Verify(task => task.Enqueue(), Times.Once);
        scheduledTask.Verify(task => task.RunNowAsync(It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(candidate => candidate.RecordRetryAsync(
            incident.Id,
            It.IsAny<DateTimeOffset>(),
            null,
            false,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task RetryAsync_FileMappingSuccess_ResolvesIncident()
    {
        var animationInfoId = Guid.NewGuid();
        var incident = CreateIncident(IncidentType.FileMappingFailure, animationInfoId.ToString());
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.FindByIdAsync(incident.Id, CancellationToken.None))
            .ReturnsAsync(incident);
        repository.Setup(candidate => candidate.RecordRetryAsync(
                incident.Id,
                It.IsAny<DateTimeOffset>(),
                null,
                true,
                CancellationToken.None))
            .ReturnsAsync(incident with { ResolvedAt = DateTimeOffset.UtcNow, RetryCount = 1 });

        var mapper = new Mock<IFileMapper>();
        mapper.Setup(candidate => candidate.MapDownloadAsync(animationInfoId, CancellationToken.None))
            .ReturnsAsync(true);
        var scopeFactory = CreateScopeFactory((typeof(IFileMapper), mapper.Object));
        var service = new IncidentRetryService(
            repository.Object,
            scopeFactory,
            [],
            Mock.Of<IIncidentDiskProbe>(),
            Mock.Of<ILogger<IncidentRetryService>>());

        var result = await service.RetryAsync(incident.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("resolved", result.Status);
        Assert.IsNotNull(result.Incident?.ResolvedAt);
    }

    [TestMethod]
    public async Task RetryAsync_AlreadyResolved_DoesNotDispatchOrIncrementRetry()
    {
        var incident = CreateIncident(IncidentType.FeedFailure, "feed") with
        {
            ResolvedAt = DateTimeOffset.UtcNow
        };
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.FindByIdAsync(incident.Id, CancellationToken.None))
            .ReturnsAsync(incident);
        var task = new Mock<IScheduledTask>();
        task.SetupGet(candidate => candidate.Id).Returns("SyncFeed");
        var service = CreateService(repository.Object, [task.Object]);

        var result = await service.RetryAsync(incident.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("resolved", result.Status);
        task.Verify(candidate => candidate.Enqueue(), Times.Never);
        repository.Verify(candidate => candidate.RecordRetryAsync(
            It.IsAny<Guid>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RetryAsync_DownloadResumeAccepted_KeepsIncidentOpenUntilProgressIsObserved()
    {
        var animationInfoId = Guid.NewGuid();
        var incident = CreateIncident(IncidentType.DownloadStalled, animationInfoId.ToString());
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.FindByIdAsync(incident.Id, CancellationToken.None))
            .ReturnsAsync(incident);
        repository.Setup(candidate => candidate.RecordRetryAsync(
                incident.Id,
                It.IsAny<DateTimeOffset>(),
                null,
                false,
                CancellationToken.None))
            .ReturnsAsync(incident with { RetryCount = 1 });

        var animationRepository = new Mock<IAnimationInfoRepository>();
        var info = new AnimationInfo(
            animationInfoId,
            "Title",
            "Description",
            DateTimeOffset.UtcNow,
            "https://example.test/item.torrent",
            "torrent",
            [],
            "hash",
            true,
            DateTimeOffset.UtcNow,
            default,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            0);
        animationRepository.Setup(candidate => candidate.FindByIdAsync(
                animationInfoId,
                CancellationToken.None))
            .ReturnsAsync(info);
        var client = new Mock<IFileDownloadClient>();
        client.Setup(candidate => candidate.ResumeDownloadTaskAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                CancellationToken.None))
            .ReturnsAsync(true);
        client.Setup(candidate => candidate.SubmitQueryDownloadProgressAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var provider = new Mock<IFileDownloadClientProvider>();
        provider.Setup(candidate => candidate.GetRequiredClient(info.DownloadType))
            .Returns(client.Object);
        var service = new IncidentRetryService(
            repository.Object,
            CreateScopeFactory(
                (typeof(IAnimationInfoRepository), animationRepository.Object),
                (typeof(IFileDownloadClientProvider), provider.Object)),
            [],
            Mock.Of<IIncidentDiskProbe>(),
            Mock.Of<ILogger<IncidentRetryService>>());

        var result = await service.RetryAsync(incident.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("queued", result.Status);
        Assert.IsNull(result.Incident?.ResolvedAt);
        repository.Verify(candidate => candidate.RecordRetryAsync(
            incident.Id,
            It.IsAny<DateTimeOffset>(),
            null,
            false,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task RetryAllAsync_MultipleFeedIncidents_QueuesSingleSharedSync()
    {
        var first = CreateIncident(IncidentType.FeedFailure, "https://example.test/one.xml");
        var second = CreateIncident(IncidentType.FeedFailure, "https://example.test/two.xml");
        var repository = new Mock<IIncidentRepository>();
        repository.Setup(candidate => candidate.GetOpenAsync(null, CancellationToken.None))
            .ReturnsAsync([first, second]);
        foreach (var incident in new[] { first, second })
        {
            repository.Setup(candidate => candidate.RecordRetryAsync(
                    incident.Id,
                    It.IsAny<DateTimeOffset>(),
                    null,
                    false,
                    CancellationToken.None))
                .ReturnsAsync(incident with { RetryCount = 1 });
        }

        var task = new Mock<IScheduledTask>();
        task.SetupGet(candidate => candidate.Id).Returns("SyncFeed");
        var service = CreateService(repository.Object, [task.Object]);

        var result = await service.RetryAllAsync(CancellationToken.None);

        Assert.AreEqual(2, result.Attempted);
        Assert.AreEqual(2, result.Succeeded);
        Assert.AreEqual(0, result.Failed);
        task.Verify(candidate => candidate.Enqueue(), Times.Once);
    }

    private static IncidentRetryService CreateService(
        IIncidentRepository repository,
        IEnumerable<IScheduledTask> tasks)
    {
        return new IncidentRetryService(
            repository,
            CreateScopeFactory(),
            tasks,
            Mock.Of<IIncidentDiskProbe>(),
            Mock.Of<ILogger<IncidentRetryService>>());
    }

    private static IServiceScopeFactory CreateScopeFactory(
        params (Type Type, object Service)[] services)
    {
        var provider = new Mock<IServiceProvider>();
        foreach (var (type, service) in services)
            provider.Setup(candidate => candidate.GetService(type)).Returns(service);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static Incident CreateIncident(IncidentType type, string sourceId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Incident(
            Guid.NewGuid(),
            "fingerprint",
            type,
            IncidentSeverity.Error,
            "Failure",
            "Something failed",
            sourceId,
            now,
            now,
            null,
            0,
            null,
            null);
    }
}
