using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class AnimationInfoControllerTests
{
    private Mock<IAnimationInfoRepository> _repoMock = null!;
    private Mock<IDistributedCache> _cacheMock = null!;
    private Mock<IFileDownloadClientProvider> _providerMock = null!;
    private Mock<IFileDownloadClient> _downloadClientMock = null!;
    private AnimationInfoController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _repoMock = new Mock<IAnimationInfoRepository>();
        _cacheMock = new Mock<IDistributedCache>();
        _providerMock = new Mock<IFileDownloadClientProvider>();
        _downloadClientMock = new Mock<IFileDownloadClient>();

        _providerMock
            .Setup(p => p.GetRequiredClient(It.IsAny<string>()))
            .Returns(_downloadClientMock.Object);

        _controller = new AnimationInfoController(
            _repoMock.Object,
            _cacheMock.Object,
            _providerMock.Object)
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
        string downloadUrl = "https://example.com/file.torrent") =>
        new(id, "title", "desc", DateTimeOffset.Now,
            downloadUrl, downloadType,
            Array.Empty<byte>(), "hash123",
            isDownloadTracked, default, default,
            isDownloadFinished, null, null,
            null, null, null, null,
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
            .Setup(c => c.SubmitDownloadTask(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo))
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

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(info);

        _downloadClientMock
            .Setup(c => c.CancelDownloadTask(
                id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                false))
            .ReturnsAsync(new CancelDownloadResult(true, false));

        var result = await _controller.CancelDownload(id, cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.UpdateAsync(
            It.Is<AnimationInfo>(i => i.Id == id && !i.IsDownloadTracked && !i.IsDownloadFinished),
            CancellationToken.None), Times.Once);
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
}
