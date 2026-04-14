using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SeasonControllerTests
{
    private Mock<ISeasonBangumiRepository> _seasonRepoMock = null!;
    private Mock<IBangumiSubgroupRepository> _subgroupRepoMock = null!;
    private Mock<IFeedRepository> _feedRepoMock = null!;
    private Mock<IHttpClientFactory> _httpClientFactoryMock = null!;
    private SeasonController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _seasonRepoMock = new Mock<ISeasonBangumiRepository>();
        _subgroupRepoMock = new Mock<IBangumiSubgroupRepository>();
        _feedRepoMock = new Mock<IFeedRepository>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        _controller = new SeasonController(
            _seasonRepoMock.Object,
            _subgroupRepoMock.Object,
            _feedRepoMock.Object,
            _httpClientFactoryMock.Object,
            Array.Empty<IScheduledTask>(),
            NullLogger<SeasonController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [TestMethod]
    public async Task Subscribe_Duplicate_ReturnsConflict()
    {
        var request = new Controllers.External.SubscribeRequest(3899, 583);

        _feedRepoMock
            .Setup(r => r.ExistsByUrlAsync(It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(true);

        var result = await _controller.Subscribe(request, CancellationToken.None);

        var conflictResult = result as ConflictObjectResult;
        Assert.IsNotNull(conflictResult);
        Assert.AreEqual(409, conflictResult.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_Success_CreatesFeedAndCallsAddAsync()
    {
        var request = new Controllers.External.SubscribeRequest(3899, null);

        _feedRepoMock
            .Setup(r => r.ExistsByUrlAsync(It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(false);

        var bangumi = new SeasonBangumi(
            Guid.NewGuid(), 3899, "Test Anime", 1, null, DateTimeOffset.UtcNow);

        _seasonRepoMock
            .Setup(r => r.FindByMikanIdAsync(3899, CancellationToken.None))
            .ReturnsAsync(bangumi);

        _feedRepoMock
            .Setup(r => r.AddAsync(It.IsAny<Feed>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        var result = await _controller.Subscribe(request, CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);

        var feed = okResult.Value as Controllers.External.Feed;
        Assert.IsNotNull(feed);
        Assert.AreEqual("Test Anime", feed.Name);
        Assert.IsTrue(feed.Url.Contains("3899"));

        _feedRepoMock.Verify(r => r.AddAsync(It.IsAny<Feed>(), CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task Refresh_RateLimit_Returns429()
    {
        _seasonRepoMock
            .Setup(r => r.GetLatestScrapedAtAsync(CancellationToken.None))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _controller.Refresh(CancellationToken.None);

        var statusResult = result as ObjectResult;
        Assert.IsNotNull(statusResult);
        Assert.AreEqual(429, statusResult.StatusCode);
    }
}
