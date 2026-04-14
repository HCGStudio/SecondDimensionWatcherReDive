using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SyncFeedTests
{
    private Mock<IAnimationInfoRepository> _mockRepo = null!;
    private SyncFeed _syncFeed = null!;
    private MethodInfo _processSingleMethod = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IAnimationInfoRepository>();

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopeServiceProvider = new Mock<IServiceProvider>();

        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockScopeServiceProvider.Object);
        mockScopeServiceProvider
            .Setup(p => p.GetService(typeof(IAnimationInfoRepository)))
            .Returns(_mockRepo.Object);

        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory.Setup(f => f.CreateClient("Feed")).Returns(new HttpClient());

        _syncFeed = new SyncFeed(
            mockServiceProvider.Object,
            Mock.Of<ILogger<SyncFeed>>(),
            mockHttpClientFactory.Object,
            mockScopeFactory.Object);

        _processSingleMethod = typeof(SyncFeed)
            .GetMethod("ProcessSingle", BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    [TestMethod]
    public async Task ProcessSingle_ExistingTitle_SkipsAdd()
    {
        var request = new AnimationAddRequest(
            DateTimeOffset.UtcNow,
            "Existing Title",
            "Description",
            "https://example.com/download",
            FileDownloadTypes.HttpDownload,
            "");

        _mockRepo
            .Setup(r => r.FindByTitleAsync("Existing Title", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnimationInfo(
                Guid.NewGuid(), "Existing Title", "Description",
                DateTimeOffset.UtcNow, "https://example.com/download",
                FileDownloadTypes.HttpDownload,
                Array.Empty<byte>(), "",
                false, default, default, false,
                null, null, null, null, null, null,
                false, 0));

        await (Task)_processSingleMethod.Invoke(
            _syncFeed, new object[] { request, CancellationToken.None })!;

        _mockRepo.Verify(
            r => r.AddAsync(It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ProcessSingle_NewTitle_AddsRecord()
    {
        var publishTime = DateTimeOffset.UtcNow;
        var request = new AnimationAddRequest(
            publishTime,
            "New Title",
            "New Description",
            "https://example.com/download",
            FileDownloadTypes.HttpDownload,
            "");

        _mockRepo
            .Setup(r => r.FindByTitleAsync("New Title", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnimationInfo?)null);

        await (Task)_processSingleMethod.Invoke(
            _syncFeed, new object[] { request, CancellationToken.None })!;

        _mockRepo.Verify(
            r => r.AddAsync(
                It.Is<AnimationInfo>(info =>
                    info.Title == "New Title" &&
                    info.Description == "New Description" &&
                    info.DownloadUrl == "https://example.com/download" &&
                    info.DownloadType == FileDownloadTypes.HttpDownload &&
                    info.PublishTime == publishTime),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
