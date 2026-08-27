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
        Animation? animation = null) =>
        new(id, "title", "desc", DateTimeOffset.Now,
            downloadUrl, downloadType,
            Array.Empty<byte>(), "hash123",
            isDownloadTracked, default, default,
            isDownloadFinished, fileStore, storePath,
            season, episode, null, animation,
            isAiProcessed, aiRetryCount);

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
        _repoMock.Verify(r => r.UpdateAsync(
            It.Is<AnimationInfo>(i => i.Id == id && i.IsDownloadTracked),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task CancelDownload_Success_SetsIsDownloadTrackedFalseAndUpdates()
    {
        var id = Guid.NewGuid();
        var info = CreateTestInfo(id, isDownloadTracked: true);
        var persistenceOrder = new List<string>();

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
            .Setup(r => r.UpdateAsync(It.IsAny<AnimationInfo>(), CancellationToken.None))
            .Callback(() => persistenceOrder.Add("state"))
            .Returns(Task.CompletedTask);
        _fileMappingRepoMock
            .Setup(r => r.RemoveByAnimationInfoAsync(id, CancellationToken.None))
            .Callback(() => persistenceOrder.Add("mappings"))
            .Returns(Task.CompletedTask);

        var result = await _controller.CancelDownload(id, cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.UpdateAsync(
            It.Is<AnimationInfo>(i => i.Id == id && !i.IsDownloadTracked && !i.IsDownloadFinished),
            CancellationToken.None), Times.Once);
        CollectionAssert.AreEqual(new[] { "state", "mappings" }, persistenceOrder);
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
