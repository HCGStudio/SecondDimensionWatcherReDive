using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Services.Transcoding;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class TranscodingControllerTests
{
    private StubTranscodingService _service = null!;
    private TranscodingController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new StubTranscodingService();
        _controller = new TranscodingController(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = CreateUrlHelper()
        };
    }

    [TestMethod]
    public async Task Prepare_QueuedJobReturnsAcceptedWithStatusAndCancelUrls()
    {
        var session = CreateStatus(TranscodingJobState.Queued, isPlayable: false);
        _service.PrepareResult = session;

        var result = await _controller.Prepare(
            new PrepareTranscodingRequest(
                Guid.NewGuid(),
                "episode.mkv",
                "720p",
                "ja",
                null,
                "en",
                null),
            CancellationToken.None);

        var accepted = Assert.IsInstanceOfType<AcceptedResult>(result);
        var response = Assert.IsInstanceOfType<TranscodingSessionResponse>(accepted.Value);
        Assert.AreEqual("queued", response.State);
        Assert.IsNull(response.PlaybackUrl);
        StringAssert.Contains(response.StatusUrl, session.SessionId.ToString());
        StringAssert.Contains(response.CancelUrl, session.AccessToken);
    }

    [TestMethod]
    public async Task Prepare_QueueFullReturns429AndRetryAfter()
    {
        _service.PrepareException = new TranscodingQueueFullException();

        var result = await _controller.Prepare(
            new PrepareTranscodingRequest(Guid.NewGuid(), "episode.mkv", null, null, null, null, null),
            CancellationToken.None);

        var response = Assert.IsInstanceOfType<ObjectResult>(result);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, response.StatusCode);
        Assert.AreEqual("5", _controller.Response.Headers.RetryAfter.ToString());
    }

    [TestMethod]
    public async Task GetPlaylist_RewritesEverySegmentWithSessionToken()
    {
        var sessionId = Guid.NewGuid();
        const string token = "secret-token";
        _service.Playlist = "#EXTM3U\n#EXTINF:6,\nsegment-000000.ts\n#EXT-X-ENDLIST\n";

        var result = await _controller.GetPlaylist(sessionId, token, CancellationToken.None);

        var content = Assert.IsInstanceOfType<ContentResult>(result);
        StringAssert.Contains(content.Content, "#EXTM3U");
        StringAssert.Contains(content.Content, "GetSegment");
        StringAssert.Contains(content.Content, "segment-000000.ts");
        StringAssert.Contains(content.Content, token);
        Assert.AreEqual("no-cache, no-store", _controller.Response.Headers.CacheControl.ToString());
    }

    private static TranscodingSessionStatus CreateStatus(
        TranscodingJobState state,
        bool isPlayable)
        => new(
            Guid.NewGuid(),
            "access-token",
            state,
            state == TranscodingJobState.Queued ? null : TranscodingStrategy.Transcode,
            isPlayable,
            false,
            null,
            null,
            state == TranscodingJobState.Queued ? 1 : null,
            null,
            null,
            null,
            [],
            0);

    private static IUrlHelper CreateUrlHelper()
    {
        var helper = new Mock<IUrlHelper>();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("example.test");
        helper.SetupGet(url => url.ActionContext)
            .Returns(new ActionContext(httpContext, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        helper.Setup(url => url.Action(It.IsAny<UrlActionContext>()))
            .Returns((UrlActionContext context) =>
            {
                var values = new RouteValueDictionary(context.Values);
                var suffix = string.Join("&", values.Select(pair => $"{pair.Key}={pair.Value}"));
                return $"https://example.test/{context.Action}?{suffix}";
            });
        return helper.Object;
    }

    private sealed class StubTranscodingService : IHlsTranscodingService
    {
        public TranscodingSessionStatus? PrepareResult { get; set; }
        public Exception? PrepareException { get; set; }
        public string? Playlist { get; set; }

        public Task<TranscodingSessionStatus> PrepareAsync(
            Guid animationInfoId,
            string? relativePath,
            TranscodingSelection selection,
            CancellationToken cancellationToken)
            => PrepareException is null
                ? Task.FromResult(PrepareResult!)
                : Task.FromException<TranscodingSessionStatus>(PrepareException);

        public Task<TranscodingSessionStatus?> GetStatusAsync(
            Guid sessionId,
            string accessToken,
            CancellationToken cancellationToken)
            => Task.FromResult<TranscodingSessionStatus?>(PrepareResult);

        public Task<string?> GetPlaylistAsync(
            Guid sessionId,
            string accessToken,
            CancellationToken cancellationToken)
            => Task.FromResult(Playlist);

        public Task<TranscodingContent?> OpenSegmentAsync(
            Guid sessionId,
            string accessToken,
            string fileName,
            CancellationToken cancellationToken)
            => Task.FromResult<TranscodingContent?>(null);

        public Task<TranscodingContent?> OpenSubtitleAsync(
            Guid sessionId,
            string accessToken,
            string fileName,
            CancellationToken cancellationToken)
            => Task.FromResult<TranscodingContent?>(null);

        public Task<TranscodingContent?> OpenDirectAsync(
            Guid sessionId,
            string accessToken,
            CancellationToken cancellationToken)
            => Task.FromResult<TranscodingContent?>(null);

        public Task<bool> CancelAsync(
            Guid sessionId,
            string accessToken,
            CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task<TranscodingMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new TranscodingMetricsSnapshot(0, 0, 0, 0, 0, 0, 0, null, null, 0));
    }
}
