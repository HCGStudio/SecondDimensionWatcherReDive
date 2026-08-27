using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.Feed;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SyncFeedTests
{
    private Mock<IAnimationInfoRepository> _mockRepo = null!;
    private Mock<ISubscriptionAutomationPolicyRepository> _mockPolicyRepo = null!;
    private Mock<IFileDownloadClientProvider> _mockDownloadProvider = null!;
    private Mock<IFileDownloadClient> _mockDownloadClient = null!;
    private SyncFeed _syncFeed = null!;
    private MethodInfo _processSingleMethod = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IAnimationInfoRepository>();
        _mockPolicyRepo = new Mock<ISubscriptionAutomationPolicyRepository>();
        _mockDownloadProvider = new Mock<IFileDownloadClientProvider>();
        _mockDownloadClient = new Mock<IFileDownloadClient>();
        _mockRepo.Setup(repository => repository.AddAsync(
                It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepo.Setup(repository => repository.UpdateAsync(
                It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopeServiceProvider = new Mock<IServiceProvider>();

        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockScopeServiceProvider.Object);
        mockScopeServiceProvider
            .Setup(p => p.GetService(typeof(IAnimationInfoRepository)))
            .Returns(_mockRepo.Object);
        mockScopeServiceProvider
            .Setup(provider => provider.GetService(typeof(ISubscriptionAutomationPolicyRepository)))
            .Returns(_mockPolicyRepo.Object);
        mockScopeServiceProvider
            .Setup(provider => provider.GetService(typeof(IFileDownloadClientProvider)))
            .Returns(_mockDownloadProvider.Object);

        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        mockHttpClientFactory.Setup(f => f.CreateClient("Feed")).Returns(new HttpClient());

        _syncFeed = new SyncFeed(
            mockServiceProvider.Object,
            Mock.Of<ILogger<SyncFeed>>(),
            mockHttpClientFactory.Object,
            mockScopeFactory.Object,
            new SubscriptionAutomationMatcher(new SubscriptionReleaseMetadataExtractor()));

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

    [TestMethod]
    public async Task ProcessSingle_PolicyDoesNotMatch_SkipsRecordAndDownload()
    {
        var feedId = Guid.NewGuid();
        var request = PolicyRequest(feedId, "[Other] Anime [1080p HEVC][CHS]");
        _mockRepo.Setup(repository => repository.FindByTitleAsync(
                request.Title, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnimationInfo?)null);
        _mockPolicyRepo.Setup(repository => repository.FindByFeedIdAsync(
                feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Policy(feedId, SubscriptionAutomationMode.AutoDownload, subtitleGroups: ["Wanted"]));

        await InvokeProcessSingleAsync(request);

        _mockRepo.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDownloadClient.Verify(client => client.SubmitDownloadTaskAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessSingle_NotifyOnly_PersistsNotifiedOutcomeAndExplanation()
    {
        var feedId = Guid.NewGuid();
        var request = PolicyRequest(feedId, "[Group] Anime [1080p HEVC][CHS]");
        SetupNewPolicyRequest(request, Policy(feedId, SubscriptionAutomationMode.NotifyOnly));
        AnimationInfo? added = null;
        _mockRepo.Setup(repository => repository.AddAsync(
                It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()))
            .Callback<AnimationInfo, CancellationToken>((info, _) => added = info)
            .Returns(Task.CompletedTask);

        await InvokeProcessSingleAsync(request);

        Assert.IsNotNull(added);
        Assert.AreEqual(SubscriptionAutomationDisposition.Notified, added.AutomationDisposition);
        Assert.AreEqual(feedId, added.SourceFeedId);
        Assert.AreEqual(request.ContentLength, added.ReleaseSizeBytes);
        StringAssert.Contains(added.AutomationExplanationJson!, "\"field\":\"subtitleGroup\"");
        StringAssert.Contains(added.AutomationExplanationJson!, "\"passed\":true");
        _mockRepo.Verify(repository => repository.UpdateAsync(
            It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessSingle_ManualConfirm_PersistsPendingConfirmationWithoutSubmitting()
    {
        var feedId = Guid.NewGuid();
        var request = PolicyRequest(feedId, "[Group] Anime [1080p HEVC][CHS]");
        SetupNewPolicyRequest(request, Policy(feedId, SubscriptionAutomationMode.ManualConfirm));

        await InvokeProcessSingleAsync(request);

        _mockRepo.Verify(repository => repository.AddAsync(
            It.Is<AnimationInfo>(info =>
                info.AutomationDisposition == SubscriptionAutomationDisposition.PendingConfirmation &&
                !info.IsDownloadTracked),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockDownloadClient.Verify(client => client.SubmitDownloadTaskAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessSingle_AutoDownload_SubmitsAndMarksQueued()
    {
        var feedId = Guid.NewGuid();
        var request = PolicyRequest(feedId, "[Group] Anime [1080p HEVC][CHS]");
        SetupNewPolicyRequest(request, Policy(feedId, SubscriptionAutomationMode.AutoDownload));
        _mockDownloadProvider.Setup(provider => provider.GetRequiredClient(request.DownloadType))
            .Returns(_mockDownloadClient.Object);
        _mockDownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                It.IsAny<Guid>(), request.DownloadUrl, It.IsAny<byte[]>(), request.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo.Setup(repository => repository.TryStartDownloadAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                SubscriptionAutomationDisposition.AutoDownloadQueued,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await InvokeProcessSingleAsync(request);

        _mockRepo.Verify(repository => repository.TryStartDownloadAsync(
            It.IsAny<Guid>(),
            It.Is<Guid>(attempt => attempt != Guid.Empty),
            It.IsAny<DateTimeOffset>(),
            SubscriptionAutomationDisposition.AutoDownloadQueued,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessSingle_AutoDownloadRejected_MarksFailed()
    {
        var feedId = Guid.NewGuid();
        var request = PolicyRequest(feedId, "[Group] Anime [1080p HEVC][CHS]");
        SetupNewPolicyRequest(request, Policy(feedId, SubscriptionAutomationMode.AutoDownload));
        _mockDownloadProvider.Setup(provider => provider.GetRequiredClient(request.DownloadType))
            .Returns(_mockDownloadClient.Object);
        _mockDownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepo.Setup(repository => repository.TryStartDownloadAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                SubscriptionAutomationDisposition.AutoDownloadQueued,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockRepo.Setup(repository => repository.TryCancelDownloadAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid?>(),
                SubscriptionAutomationDisposition.AutoDownloadFailed,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnimationInfo?)null);

        await InvokeProcessSingleAsync(request);

        _mockRepo.Verify(repository => repository.TryCancelDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            SubscriptionAutomationDisposition.AutoDownloadFailed,
            It.Is<CancellationToken>(token =>
                token.CanBeCanceled && !token.IsCancellationRequested)), Times.Once);
    }

    private void SetupNewPolicyRequest(AnimationAddRequest request, SubscriptionAutomationPolicy policy)
    {
        _mockRepo.Setup(repository => repository.FindByTitleAsync(
                request.Title, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnimationInfo?)null);
        _mockPolicyRepo.Setup(repository => repository.FindByFeedIdAsync(
                policy.FeedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
    }

    private async Task InvokeProcessSingleAsync(AnimationAddRequest request)
    {
        await (Task)_processSingleMethod.Invoke(
            _syncFeed, new object[] { request, CancellationToken.None })!;
    }

    private static AnimationAddRequest PolicyRequest(Guid feedId, string title)
    {
        return new AnimationAddRequest(
            DateTimeOffset.UtcNow,
            title,
            string.Empty,
            "https://example.com/release",
            FileDownloadTypes.HttpDownload,
            "download-key",
            feedId,
            1_000_000_000);
    }

    private static SubscriptionAutomationPolicy Policy(
        Guid feedId,
        SubscriptionAutomationMode mode,
        IReadOnlyList<string>? subtitleGroups = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SubscriptionAutomationPolicy(
            feedId,
            subtitleGroups ?? [],
            [],
            [],
            [],
            null,
            null,
            [],
            mode,
            now,
            now);
    }
}
