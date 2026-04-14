using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class FeedControllerTests
{
    private Mock<IFeedRepository> _repoMock = null!;
    private FeedController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _repoMock = new Mock<IFeedRepository>();

        _controller = new FeedController(_repoMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [TestMethod]
    public async Task GetFeeds_ReturnsAll()
    {
        var feeds = new List<Feed>
        {
            new(Guid.NewGuid(), "https://example.com/rss1", "Feed 1", DateTimeOffset.Now),
            new(Guid.NewGuid(), "https://example.com/rss2", "Feed 2", DateTimeOffset.Now)
        };

        _repoMock
            .Setup(r => r.GetAllOrderedAsync(CancellationToken.None))
            .ReturnsAsync(feeds);

        var result = await _controller.GetFeeds(CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);
        Assert.AreEqual(200, okResult.StatusCode);

        var returnedFeeds = okResult.Value as List<Controllers.External.Feed>;
        Assert.IsNotNull(returnedFeeds);
        Assert.AreEqual(2, returnedFeeds.Count);
    }

    [TestMethod]
    public async Task AddFeed_Success_ReturnsOkWithFeed()
    {
        var request = new Controllers.External.AddFeedRequest("https://example.com/rss", "My Feed");

        _repoMock
            .Setup(r => r.AddAsync(It.IsAny<Feed>(), CancellationToken.None))
            .Returns(Task.CompletedTask);

        var result = await _controller.AddFeed(request, CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);

        var feed = okResult.Value as Controllers.External.Feed;
        Assert.IsNotNull(feed);
        Assert.AreEqual("https://example.com/rss", feed.Url);
        Assert.AreEqual("My Feed", feed.Name);
        Assert.AreNotEqual(Guid.Empty, feed.Id);

        _repoMock.Verify(r => r.AddAsync(It.IsAny<Feed>(), CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task RemoveFeed_NotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync((Feed?)null);

        var result = await _controller.RemoveFeed(id, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task RemoveFeed_Success_ReturnsOk()
    {
        var id = Guid.NewGuid();
        var feed = new Feed(id, "https://example.com/rss", "Feed", DateTimeOffset.Now);

        _repoMock
            .Setup(r => r.FindByIdAsync(id, CancellationToken.None))
            .ReturnsAsync(feed);

        _repoMock
            .Setup(r => r.RemoveAsync(feed, CancellationToken.None))
            .Returns(Task.CompletedTask);

        var result = await _controller.RemoveFeed(id, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.RemoveAsync(feed, CancellationToken.None), Times.Once);
    }
}
