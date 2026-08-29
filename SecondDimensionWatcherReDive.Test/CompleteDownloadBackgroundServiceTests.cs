using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class CompleteDownloadBackgroundServiceTests
{
    [TestMethod]
    public async Task ProcessRequestAsync_CancelledDownload_IgnoresLateCompletion()
    {
        var request = new DownloadCompleteRequest(
            Guid.NewGuid(),
            "/downloads/item",
            "local",
            Guid.NewGuid());
        var repository = new Mock<IAnimationInfoRepository>();
        repository.Setup(candidate => candidate.TryCompleteDownloadAsync(
                request.ItemId,
                request.DownloadAttemptId,
                request.FileStore,
                request.StorePath,
                It.IsAny<DateTimeOffset>(),
                CancellationToken.None))
            .ReturnsAsync((AnimationInfo?)null);
        var mapper = new Mock<IFileMapper>();
        var plugin = new Mock<IPluginEventTrigger<FileDownloadCompleteParam>>();
        var reporter = new Mock<IIncidentReporter>();
        using var provider = CreateProvider(repository.Object, mapper.Object, plugin.Object);
        var service = new CompleteDownloadBackgroundService(
            Channel.CreateUnbounded<DownloadCompleteRequest>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<CompleteDownloadBackgroundService>>(),
            reporter.Object);

        await service.ProcessRequestAsync(request, CancellationToken.None);

        mapper.Verify(candidate => candidate.MapDownloadAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        plugin.Verify(candidate => candidate.InvokeAsync(
            It.IsAny<FileDownloadCompleteParam>(), It.IsAny<CancellationToken>()), Times.Never);
        reporter.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ProcessRequestAsync_TrackedDownload_CompletesBeforeMappingAndPlugin()
    {
        var request = new DownloadCompleteRequest(
            Guid.NewGuid(),
            "/downloads/item",
            "local",
            Guid.NewGuid());
        var info = CreateInfo(request.ItemId);
        var repository = new Mock<IAnimationInfoRepository>();
        repository.Setup(candidate => candidate.TryCompleteDownloadAsync(
                request.ItemId,
                request.DownloadAttemptId,
                request.FileStore,
                request.StorePath,
                It.IsAny<DateTimeOffset>(),
                CancellationToken.None))
            .ReturnsAsync(info);
        var mapper = new Mock<IFileMapper>();
        mapper.Setup(candidate => candidate.MapDownloadAsync(
                request.ItemId,
                CancellationToken.None))
            .ReturnsAsync(true);
        var plugin = new Mock<IPluginEventTrigger<FileDownloadCompleteParam>>();
        plugin.Setup(candidate => candidate.InvokeAsync(
                It.IsAny<FileDownloadCompleteParam>(),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        using var provider = CreateProvider(repository.Object, mapper.Object, plugin.Object);
        var notifications = new Mock<INotificationPublisher>();
        var service = new CompleteDownloadBackgroundService(
            Channel.CreateUnbounded<DownloadCompleteRequest>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<CompleteDownloadBackgroundService>>(),
            Mock.Of<IIncidentReporter>(),
            notifications.Object);

        await service.ProcessRequestAsync(request, CancellationToken.None);

        mapper.Verify(candidate => candidate.MapDownloadAsync(
            request.ItemId, CancellationToken.None), Times.Once);
        plugin.Verify(candidate => candidate.InvokeAsync(
            It.Is<FileDownloadCompleteParam>(parameter =>
                parameter.ItemId == request.ItemId
                && parameter.StorePath == request.StorePath
                && parameter.FileStore == request.FileStore),
            CancellationToken.None), Times.Once);
        notifications.Verify(candidate => candidate.PublishAsync(
            It.Is<NotificationEvent>(notification =>
                notification.Type == NotificationEventType.DownloadCompleted
                && notification.DeduplicationKey.Contains(request.ItemId.ToString(), StringComparison.Ordinal)),
            CancellationToken.None), Times.Once);
    }

    private static ServiceProvider CreateProvider(
        IAnimationInfoRepository repository,
        IFileMapper mapper,
        IPluginEventTrigger<FileDownloadCompleteParam> plugin)
    {
        return new ServiceCollection()
            .AddSingleton(repository)
            .AddSingleton(mapper)
            .AddSingleton(plugin)
            .BuildServiceProvider();
    }

    private static AnimationInfo CreateInfo(Guid id) => new(
        id,
        "Title",
        "Description",
        DateTimeOffset.UtcNow,
        "https://example.test/item.torrent",
        "torrent",
        [],
        "hash",
        true,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        true,
        "local",
        "/downloads/item",
        1,
        1,
        null,
        null,
        true,
        0,
        AutomationDisposition: SubscriptionAutomationDisposition.DownloadCompleted);
}
