using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class AnimationInfoControllerTests
{
    private Mock<IAnimationInfoRepository> _repoMock = null!;
    private Mock<IFileMappingRepository> _fileMappingRepoMock = null!;
    private Mock<IDistributedCache> _cacheMock = null!;
    private Mock<IFileDownloadClientProvider> _providerMock = null!;
    private Mock<IFileDownloadClient> _downloadClientMock = null!;
    private Mock<IFileMapper> _fileMapperMock = null!;
    private AnimationInfoController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _repoMock = new Mock<IAnimationInfoRepository>();
        _fileMappingRepoMock = new Mock<IFileMappingRepository>();
        _cacheMock = new Mock<IDistributedCache>();
        _providerMock = new Mock<IFileDownloadClientProvider>();
        _downloadClientMock = new Mock<IFileDownloadClient>();
        _fileMapperMock = new Mock<IFileMapper>();

        _providerMock
            .Setup(p => p.GetRequiredClient(It.IsAny<string>()))
            .Returns(_downloadClientMock.Object);

        _controller = new AnimationInfoController(
            _repoMock.Object,
            _fileMappingRepoMock.Object,
            _cacheMock.Object,
            _providerMock.Object,
            _fileMapperMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static AnimationInfo CreateTestInfo(
        Guid id,
        bool isDownloadTracked = false,
        bool isDownloadFinished = false,
        bool isAiProcessed = false,
        int aiRetryCount = 0,
        string downloadType = "torrent",
        string downloadUrl = "https://example.com/file.torrent",
        string? fileStore = null,
        string? storePath = null,
        int? season = null,
        int? episode = null,
        Animation? animation = null,
        AnimationGroup? group = null,
        SubscriptionAutomationDisposition? automationDisposition = null) =>
        new(id, "title", "desc", DateTimeOffset.Now,
            downloadUrl, downloadType,
            Array.Empty<byte>(), "hash123",
            isDownloadTracked, default, default,
            isDownloadFinished, fileStore, storePath,
            season, episode, group, animation,
            isAiProcessed, aiRetryCount,
            AutomationDisposition: automationDisposition);

    [TestMethod]
    public async Task StartDownload_NotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync((AnimationInfo?)null);

        var result = await _controller.StartDownload(id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task StartDownload_AlreadyTracked_ReturnsConflict()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id, isDownloadTracked: true);

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);

        var result = await _controller.StartDownload(id, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictResult>(result);
    }

    [TestMethod]
    public async Task StartDownload_Success_ReturnsOkAndUpdatesEntity()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id);

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _repoMock
            .Setup(r => r.TryStartDownloadAsync(
                id,
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                null,
                CancellationToken.None))
            .ReturnsAsync(true);

        _downloadClientMock
            .Setup(c => c.SubmitDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                CancellationToken.None))
            .ReturnsAsync(true);

        var result = await _controller.StartDownload(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.TryStartDownloadAsync(
            id,
            It.Is<Guid>(attempt => attempt != Guid.Empty),
            It.IsAny<DateTimeOffset>(),
            null,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task StartDownload_PendingConfirmation_MarksManualDownloadQueued()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(
            id,
            automationDisposition: SubscriptionAutomationDisposition.PendingConfirmation);

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _repoMock
            .Setup(r => r.TryStartDownloadAsync(
                id,
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                null,
                CancellationToken.None))
            .ReturnsAsync(true);
        _downloadClientMock
            .Setup(c => c.SubmitDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                CancellationToken.None))
            .ReturnsAsync(true);

        var result = await _controller.StartDownload(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.TryStartDownloadAsync(
            id,
            It.IsAny<Guid>(),
            It.IsAny<DateTimeOffset>(),
            null,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task StartDownload_ClientRejects_ReleasesReservedAttempt()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id);
        Guid? reservedAttempt = null;
        _repoMock.Setup(repository => repository.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _repoMock.Setup(repository => repository.TryStartDownloadAsync(
                id,
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                null,
                CancellationToken.None))
            .Callback<Guid, Guid, DateTimeOffset, SubscriptionAutomationDisposition?, CancellationToken>(
                (_, attempt, _, _, _) => reservedAttempt = attempt)
            .ReturnsAsync(true);
        _downloadClientMock.Setup(client => client.SubmitDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                CancellationToken.None))
            .ReturnsAsync(false);
        _repoMock.Setup(repository => repository.TryCancelDownloadAsync(
                id,
                It.IsAny<Guid?>(),
                null,
                It.Is<CancellationToken>(token =>
                    token.CanBeCanceled && !token.IsCancellationRequested)))
            .ReturnsAsync(info with { IsDownloadTracked = false });

        var result = await _controller.StartDownload(id, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestResult>(result);
        Assert.IsNotNull(reservedAttempt);
        _repoMock.Verify(repository => repository.TryCancelDownloadAsync(
            id,
            reservedAttempt,
            null,
            It.Is<CancellationToken>(token =>
                token.CanBeCanceled && !token.IsCancellationRequested)), Times.Once);
    }

    [TestMethod]
    public async Task StartDownload_ReservationCancellation_UsesIndependentCleanupToken()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id);
        _repoMock.Setup(repository => repository.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _repoMock.Setup(repository => repository.TryStartDownloadAsync(
                id,
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                null,
                CancellationToken.None))
            .ThrowsAsync(new OperationCanceledException());
        _repoMock.Setup(repository => repository.TryCancelDownloadAsync(
                id,
                It.IsAny<Guid?>(),
                null,
                It.Is<CancellationToken>(token =>
                    token.CanBeCanceled && !token.IsCancellationRequested)))
            .ReturnsAsync(info with { IsDownloadTracked = false });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            _controller.StartDownload(id, CancellationToken.None));

        _repoMock.Verify(repository => repository.TryCancelDownloadAsync(
            id,
            It.IsAny<Guid?>(),
            null,
            It.Is<CancellationToken>(token =>
                token.CanBeCanceled && !token.IsCancellationRequested)), Times.Once);
        _downloadClientMock.Verify(client => client.SubmitDownloadTaskAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task StartDownload_IdentifiedItem_PreservesAnimationAndGroup()
    {
        var id = Guid.NewGuid();
        var animation = new Animation(Guid.NewGuid(), "1234", "A Show", "Original", null);
        var group = new AnimationGroup(Guid.NewGuid(), "Sub Group");
        var info = CreateTestInfo(id, animation: animation, group: group);
        _repoMock.Setup(repository => repository.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _repoMock.Setup(repository => repository.TryStartDownloadAsync(
                id,
                It.IsAny<Guid>(),
                It.IsAny<DateTimeOffset>(),
                null,
                CancellationToken.None))
            .ReturnsAsync(true);
        _downloadClientMock.Setup(client => client.SubmitDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                CancellationToken.None))
            .ReturnsAsync(true);

        var result = await _controller.StartDownload(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(repository => repository.UpdateAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CancelDownload_Success_SetsIsDownloadTrackedFalseAndUpdates()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id, isDownloadTracked: true);
        Guid? cancellationAttemptId = null;

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);

        _downloadClientMock
            .Setup(c => c.CancelDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                false,
                CancellationToken.None))
            .ReturnsAsync(new CancelDownloadResult(true, false));
        _repoMock
            .Setup(r => r.TryBeginCancelDownloadAsync(
                id,
                info.DownloadAttemptId,
                It.IsAny<Guid>(),
                It.Is<CancellationToken>(token =>
                    token.CanBeCanceled && !token.IsCancellationRequested)))
            .Callback<Guid, Guid?, Guid, CancellationToken>(
                (_, _, cancellationId, _) => cancellationAttemptId = cancellationId)
            .ReturnsAsync(true);
        _fileMappingRepoMock
            .Setup(r => r.TryFinalizeDownloadCancellationAsync(
                id,
                info.DownloadAttemptId,
                It.IsAny<Guid>(),
                null,
                It.Is<CancellationToken>(token =>
                    token.CanBeCanceled && !token.IsCancellationRequested)))
            .ReturnsAsync(true);

        var result = await _controller.CancelDownload(id, cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        Assert.IsNotNull(cancellationAttemptId);
        _fileMappingRepoMock.Verify(r => r.TryFinalizeDownloadCancellationAsync(
            id,
            info.DownloadAttemptId,
            cancellationAttemptId.Value,
            null,
            It.Is<CancellationToken>(token =>
                token.CanBeCanceled && !token.IsCancellationRequested)), Times.Once);
    }

    [TestMethod]
    public async Task CancelDownload_AutomaticDownload_MarksDispositionCancelled()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(
            id,
            isDownloadTracked: true,
            automationDisposition: SubscriptionAutomationDisposition.AutoDownloadQueued);
        _repoMock.Setup(r => r.FindByIdAsync(id, CancellationToken.None)).ReturnsAsync(info);
        _downloadClientMock.Setup(c => c.CancelDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                false,
                CancellationToken.None))
            .ReturnsAsync(new CancelDownloadResult(true, false));
        _repoMock.Setup(r => r.TryBeginCancelDownloadAsync(
                id,
                info.DownloadAttemptId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileMappingRepoMock.Setup(r => r.TryFinalizeDownloadCancellationAsync(
                id,
                info.DownloadAttemptId,
                It.IsAny<Guid>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.CancelDownload(id, cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _fileMappingRepoMock.Verify(r => r.TryFinalizeDownloadCancellationAsync(
            id,
            info.DownloadAttemptId,
            It.IsAny<Guid>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CancelDownload_PendingCancellation_ReusesIdAndFinalizes()
    {
        var id = Guid.NewGuid();
        var cancellationAttemptId = Guid.NewGuid();
        var info = CreateTestInfo(id, isDownloadTracked: true) with
        {
            DownloadAttemptId = Guid.NewGuid(),
            DownloadCancellationId = cancellationAttemptId
        };
        _repoMock.Setup(repository => repository.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _repoMock.Setup(repository => repository.TryBeginCancelDownloadAsync(
                id,
                info.DownloadAttemptId,
                cancellationAttemptId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _downloadClientMock.Setup(client => client.CancelDownloadTaskAsync(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                false,
                CancellationToken.None))
            .ReturnsAsync(new CancelDownloadResult(true, false));
        _fileMappingRepoMock.Setup(repository => repository.TryFinalizeDownloadCancellationAsync(
                id,
                info.DownloadAttemptId,
                cancellationAttemptId,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.CancelDownload(id, cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(repository => repository.TryBeginCancelDownloadAsync(
            id,
            info.DownloadAttemptId,
            cancellationAttemptId,
            It.IsAny<CancellationToken>()), Times.Once);
        _fileMappingRepoMock.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            id,
            info.DownloadAttemptId,
            cancellationAttemptId,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task RetryInference_NotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync((AnimationInfo?)null);

        var result = await _controller.RetryInference(id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task RetryInference_Success_ResetsFieldsAndUpdates()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id, isAiProcessed: true, aiRetryCount: 3);

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);

        var result = await _controller.RetryInference(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.UpdateAsync(
            It.Is<AnimationInfo>(i => i.Id == id && !i.IsAiProcessed && i.AiRetryCount == 0),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAi_NotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repoMock
            .Setup(r => r.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync((AnimationInfo?)null);

        var result = await _controller.ReidentifyFilesWithAi(id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
        _fileMapperMock.Verify(
            m => m.ReidentifyFilesWithAiAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAi_NotDownloaded_ReturnsConflict()
    {
        var id = Guid.NewGuid();
        var info = CreateMultiEpisodeDownload(id) with { IsDownloadFinished = false };
        _repoMock
            .Setup(r => r.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync(info);

        var result = await _controller.ReidentifyFilesWithAi(id, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictResult>(result);
        _fileMapperMock.Verify(
            m => m.ReidentifyFilesWithAiAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAi_SingleEpisode_ReturnsConflict()
    {
        var id = Guid.NewGuid();
        var info = CreateMultiEpisodeDownload(id) with { Episode = 1 };
        _repoMock
            .Setup(r => r.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync(info);

        var result = await _controller.ReidentifyFilesWithAi(id, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictResult>(result);
        _fileMapperMock.Verify(
            m => m.ReidentifyFilesWithAiAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAi_Success_ReturnsOkAndInvokesMapper()
    {
        var id = Guid.NewGuid();
        var info = CreateMultiEpisodeDownload(id);
        _repoMock
            .Setup(r => r.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMapperMock
            .Setup(m => m.ReidentifyFilesWithAiAsync(id, CancellationToken.None))
            .ReturnsAsync(true);

        var result = await _controller.ReidentifyFilesWithAi(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _fileMapperMock.Verify(
            m => m.ReidentifyFilesWithAiAsync(id, CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAi_NoRecognizedFiles_ReturnsUnprocessableEntity()
    {
        var id = Guid.NewGuid();
        var info = CreateMultiEpisodeDownload(id);
        _repoMock
            .Setup(r => r.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMapperMock
            .Setup(m => m.ReidentifyFilesWithAiAsync(id, CancellationToken.None))
            .ReturnsAsync(false);

        var result = await _controller.ReidentifyFilesWithAi(id, CancellationToken.None);

        var unprocessable = Assert.IsInstanceOfType<UnprocessableEntityResult>(result);
        Assert.AreEqual(StatusCodes.Status422UnprocessableEntity, unprocessable.StatusCode);
    }

    [TestMethod]
    public async Task ReidentifyFilesWithAi_AiUnavailable_ReturnsServiceUnavailable()
    {
        var id = Guid.NewGuid();
        var info = CreateMultiEpisodeDownload(id);
        _repoMock
            .Setup(r => r.FindByIdWithAnimationAsync(id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMapperMock
            .Setup(m => m.ReidentifyFilesWithAiAsync(id, CancellationToken.None))
            .ThrowsAsync(new AiFileNameInferenceUnavailableException());

        var result = await _controller.ReidentifyFilesWithAi(id, CancellationToken.None);

        var unavailable = Assert.IsInstanceOfType<StatusCodeResult>(result);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
    }

    private static AnimationInfo CreateMultiEpisodeDownload(Guid id) =>
        CreateTestInfo(
            id,
            isDownloadFinished: true,
            fileStore: "local",
            storePath: "/downloads/anime",
            season: 1,
            animation: new Animation(Guid.NewGuid(), "123", "Anime", "Anime", null));
}
