using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class CompleteDownloadBackgroundServiceTests
{
    [TestMethod]
    public async Task ProcessRequest_AutomaticDownload_MarksDispositionCompleted()
    {
        var id = Guid.NewGuid();
        var info = new AnimationInfo(
            id,
            "Anime",
            string.Empty,
            DateTimeOffset.UtcNow,
            "https://example.com/release.torrent",
            "torrent",
            [],
            "hash",
            IsDownloadTracked: true,
            DownloadStartTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            DownloadEndTime: default,
            IsDownloadFinished: false,
            FileStore: null,
            StorePath: null,
            Season: null,
            Episode: null,
            Group: null,
            Animation: null,
            IsAiProcessed: false,
            AiRetryCount: 0,
            AutomationDisposition: SubscriptionAutomationDisposition.AutoDownloadQueued);
        var repository = new Mock<IAnimationInfoRepository>();
        repository.Setup(item => item.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        var fileMapper = new Mock<IFileMapper>();
        fileMapper.Setup(item => item.MapDownloadAsync(id, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var eventTrigger = new Mock<IPluginEventTrigger<FileDownloadCompleteParam>>();
        eventTrigger.Setup(item => item.InvokeAsync(
                It.IsAny<FileDownloadCompleteParam>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(provider => provider.GetService(typeof(IAnimationInfoRepository)))
            .Returns(repository.Object);
        scopedProvider.Setup(provider => provider.GetService(typeof(IFileMapper)))
            .Returns(fileMapper.Object);
        scopedProvider.Setup(provider => provider.GetService(
                typeof(IPluginEventTrigger<FileDownloadCompleteParam>)))
            .Returns(eventTrigger.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(item => item.ServiceProvider).Returns(scopedProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(item => item.CreateScope()).Returns(scope.Object);
        var service = new CompleteDownloadBackgroundService(
            Channel.CreateUnbounded<DownloadCompleteRequest>(),
            scopeFactory.Object,
            Mock.Of<ILogger<CompleteDownloadBackgroundService>>());
        var processRequest = typeof(CompleteDownloadBackgroundService)
            .GetMethod("ProcessRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)processRequest.Invoke(service, [
            new DownloadCompleteRequest(id, "/downloads/anime", "local"),
            CancellationToken.None
        ])!;

        repository.Verify(item => item.UpdateAsync(
            It.Is<AnimationInfo>(updated =>
                updated.IsDownloadFinished &&
                updated.AutomationDisposition == SubscriptionAutomationDisposition.DownloadCompleted &&
                updated.FileStore == "local" &&
                updated.StorePath == "/downloads/anime"),
            CancellationToken.None), Times.Once);
    }
}
