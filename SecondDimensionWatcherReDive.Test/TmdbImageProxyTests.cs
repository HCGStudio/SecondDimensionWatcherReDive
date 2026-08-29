using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class TmdbImageProxyTests
{
    private static readonly byte[] ImageBytes = [0xff, 0xd8, 0xff, 0xd9];

    [TestMethod]
    public async Task Service_CachesSuccessfulImagesAndUsesFixedRelativePath()
    {
        var handler = new RecordingHandler(_ => ImageResponse(ImageBytes));
        using var service = CreateService(handler);

        var first = await service.GetAsync("w300", "poster.jpg", CancellationToken.None);
        var second = await service.GetAsync("w300", "poster.jpg", CancellationToken.None);

        Assert.AreEqual(TmdbImageFetchStatus.Success, first.Status);
        CollectionAssert.AreEqual(ImageBytes, first.Content!.Bytes);
        Assert.AreEqual("image/jpeg", first.Content.ContentType);
        Assert.AreEqual(first.Content.ETag, second.Content!.ETag);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.AreEqual(
            "https://image.tmdb.org/t/p/w300/poster.jpg",
            handler.LastRequestUri!.AbsoluteUri);
    }

    [TestMethod]
    public async Task Service_RejectsUnapprovedPathsWithoutCallingUpstream()
    {
        var handler = new RecordingHandler(_ => ImageResponse(ImageBytes));
        using var service = CreateService(handler);

        var invalidSize = await service.GetAsync("w999", "poster.jpg", CancellationToken.None);
        var traversal = await service.GetAsync("w300", "..poster.jpg", CancellationToken.None);
        var unsupportedExtension = await service.GetAsync("w300", "poster.svg", CancellationToken.None);

        Assert.AreEqual(TmdbImageFetchStatus.InvalidPath, invalidSize.Status);
        Assert.AreEqual(TmdbImageFetchStatus.InvalidPath, traversal.Status);
        Assert.AreEqual(TmdbImageFetchStatus.InvalidPath, unsupportedExtension.Status);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task Service_DoesNotCacheAnEntryLargerThanCacheBudget()
    {
        var handler = new RecordingHandler(_ => ImageResponse(ImageBytes));
        using var service = CreateService(handler, cacheSizeBytes: 3, maxImageBytes: 16);

        await service.GetAsync("w185", "poster.jpg", CancellationToken.None);
        await service.GetAsync("w185", "poster.jpg", CancellationToken.None);

        Assert.AreEqual(2, handler.RequestCount);
    }

    [TestMethod]
    public async Task Service_RejectsOversizedOrNonImageResponses()
    {
        var oversizedHandler = new RecordingHandler(_ => ImageResponse(new byte[5]));
        using var oversizedService = CreateService(
            oversizedHandler,
            cacheSizeBytes: 64,
            maxImageBytes: 4);

        var oversized = await oversizedService.GetAsync(
            "w185",
            "poster.jpg",
            CancellationToken.None);

        var textHandler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not an image")
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return response;
        });
        using var textService = CreateService(textHandler);
        var text = await textService.GetAsync("w185", "poster.jpg", CancellationToken.None);

        Assert.AreEqual(TmdbImageFetchStatus.Unavailable, oversized.Status);
        Assert.AreEqual(TmdbImageFetchStatus.Unavailable, text.Status);
    }

    [TestMethod]
    public async Task Controller_SetsCacheHeadersAndHonorsConditionalRequest()
    {
        var content = new TmdbImageContent(ImageBytes, "image/jpeg", "\"image-etag\"");
        var proxy = new StubImageProxy(
            new TmdbImageFetchResult(TmdbImageFetchStatus.Success, content));
        var controller = CreateController(proxy);

        var first = await controller.GetAsync("w300", "poster.jpg", CancellationToken.None);

        Assert.IsInstanceOfType<FileContentResult>(first);
        Assert.AreEqual("public, max-age=86400", controller.Response.Headers.CacheControl.ToString());
        Assert.AreEqual(content.ETag, controller.Response.Headers.ETag.ToString());
        Assert.AreEqual("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());

        controller = CreateController(proxy);
        controller.Request.Headers.IfNoneMatch = content.ETag;
        var conditional = await controller.GetAsync("w300", "poster.jpg", CancellationToken.None);

        Assert.IsInstanceOfType<StatusCodeResult>(conditional);
        Assert.AreEqual(StatusCodes.Status304NotModified, ((StatusCodeResult)conditional).StatusCode);
    }

    [TestMethod]
    public async Task Controller_ReturnsStructuredFailureCodeWithoutCachingFailure()
    {
        var proxy = new StubImageProxy(
            new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable));
        var controller = CreateController(proxy);

        var result = await controller.GetAsync("w300", "bad.jpg", CancellationToken.None);

        var objectResult = (ObjectResult)result;
        Assert.AreEqual(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        var problem = (ProblemDetails)objectResult.Value!;
        Assert.AreEqual("tmdb_image_unavailable", problem.Extensions["code"]);
        Assert.AreEqual("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    private static TmdbImageProxyService CreateService(
        HttpMessageHandler handler,
        long cacheSizeBytes = 1024,
        int maxImageBytes = 512)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://image.tmdb.org/t/p/")
        };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient("TmdbImages")).Returns(client);
        return new TmdbImageProxyService(
            factory.Object,
            Options.Create(new TmdbImageProxyOptions
            {
                CacheSizeBytes = cacheSizeBytes,
                MaxImageBytes = maxImageBytes,
                CacheDuration = TimeSpan.FromDays(1),
                ClientCacheDuration = TimeSpan.FromDays(1)
            }),
            NullLogger<TmdbImageProxyService>.Instance);
    }

    private static TmdbImagesController CreateController(ITmdbImageProxyService proxy) =>
        new(
            proxy,
            Options.Create(new TmdbImageProxyOptions
            {
                ClientCacheDuration = TimeSpan.FromDays(1)
            }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static HttpResponseMessage ImageResponse(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StubImageProxy(TmdbImageFetchResult result) : ITmdbImageProxyService
    {
        public Task<TmdbImageFetchResult> GetAsync(
            string size,
            string fileName,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
