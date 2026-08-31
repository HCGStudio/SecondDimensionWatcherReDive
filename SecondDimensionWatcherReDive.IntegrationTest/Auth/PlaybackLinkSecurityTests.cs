using System.Net;
using System.Net.Http.Json;
using Moq;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.IntegrationTest.Auth;

[TestClass]
public sealed class PlaybackLinkSecurityTests
{
    [TestMethod]
    public async Task PlaybackCredential_StaysInHttpOnlyCookie_AndNeverAppearsInRequestLogs()
    {
        await using var factory = new WebDavWebApplicationFactory();
        factory.ResetState();
        var animationId = Guid.NewGuid();
        const string VirtualPath = "/unknown/episode.mp4";
        factory.Mappings.Add(new Framework.DataRepository.FileMapping(
            Guid.NewGuid(), animationId, VirtualPath, "/physical/episode.mp4", "local"));
        factory.AnimationInfoRepositoryMock
            .Setup(repository => repository.FindByIdWithAnimationAsync(
                animationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDownloadedAnimation(animationId));
        factory.FileStoreMock
            .Setup(store => store.OpenReadStreamAsync(
                "/physical/episode.mp4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        using var client = factory.CreateJwtClient();
        client.BaseAddress = new Uri("https://localhost");

        using var generated = await client.PostAsJsonAsync(
            "/api/file/generateLink",
            new { id = animationId, path = "episode.mp4" });
        generated.EnsureSuccessStatusCode();
        Assert.Contains("no-store", generated.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        var link = await generated.Content.ReadFromJsonAsync<FileLinkResultResponse>();
        Assert.IsNotNull(link);
        Assert.IsFalse(link.Url.Contains('?', StringComparison.Ordinal));
        Assert.IsNull(link.ExternalUrl);

        var setCookie = generated.Headers.GetValues("Set-Cookie").Single();
        Assert.IsTrue(setCookie.StartsWith("__Host-sdw-playback=", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", setCookie, StringComparison.OrdinalIgnoreCase);
        var separator = setCookie.IndexOf('=');
        var cookieName = setCookie[..separator];
        var cookieCredential = setCookie[(separator + 1)..setCookie.IndexOf(';')];
        Assert.IsFalse(link.Url.Contains(cookieCredential, StringComparison.Ordinal));

        // TestServer's in-memory handler does not share a browser CookieContainer. Send the
        // Set-Cookie value back exactly as a browser would, with no bearer authorization.
        client.DefaultRequestHeaders.Authorization = null;
        using var playRequest = new HttpRequestMessage(HttpMethod.Get, link.Url);
        playRequest.Headers.Add("Cookie", $"{cookieName}={cookieCredential}");
        using var played = await client.SendAsync(playRequest);
        Assert.AreEqual(HttpStatusCode.OK, played.StatusCode);
        using var withoutCookie = factory.CreateUnauthenticatedClient();
        using var rejected = await withoutCookie.GetAsync(link.Url);
        Assert.AreEqual(HttpStatusCode.NotFound, rejected.StatusCode);

        var requestTarget = new Uri(link.Url).PathAndQuery;
        Assert.IsTrue(factory.Logs.Messages.Any(message =>
            message.Contains("Request starting", StringComparison.Ordinal) &&
            message.Contains(requestTarget, StringComparison.Ordinal)));
        Assert.IsFalse(factory.Logs.Messages.Any(message =>
            message.Contains(cookieCredential, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PlaybackTickets_WorkAcrossReplicasWithSharedKeyRing_AndStaySessionBound()
    {
        await using var firstFactory = new WebDavWebApplicationFactory();
        await using var secondFactory = new WebDavWebApplicationFactory();
        var animationId = Guid.NewGuid();
        const string VideoPath = "/unknown/episode.mp4";
        const string SubtitlePath = "/unknown/episode.zh.srt";
        ConfigurePlayback(firstFactory, animationId, VideoPath, SubtitlePath);
        ConfigurePlayback(secondFactory, animationId, VideoPath, SubtitlePath);

        using var issuingClient = firstFactory.CreateJwtClient();
        issuingClient.BaseAddress = new Uri("https://localhost");
        var generated = await Task.WhenAll(
            GenerateAsync(issuingClient, animationId, "episode.mp4"),
            GenerateAsync(issuingClient, animationId, "episode.zh.srt"));
        var video = generated[0];
        var subtitle = generated[1];
        var cookie = ReadCookie(video.Response);
        var subtitleCookie = ReadCookie(subtitle.Response);
        Assert.AreEqual(cookie.Name, subtitleCookie.Name);

        // A different process/app service provider can validate both resources from the
        // shared key ring. Concurrent Set-Cookie responses are interchangeable because
        // both requests used the same authenticated access-token session.
        using var replicaClient = secondFactory.CreateUnauthenticatedClient();
        using var videoResponse = await GetWithCookieAsync(replicaClient, video.Link.Url, subtitleCookie);
        using var subtitleResponse = await GetWithCookieAsync(replicaClient, subtitle.Link.Url, cookie);
        Assert.AreEqual(HttpStatusCode.OK, videoResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, subtitleResponse.StatusCode);

        // A cookie issued to another login session cannot be paired with either resource.
        using var otherSessionClient = firstFactory.CreateJwtClient();
        otherSessionClient.BaseAddress = new Uri("https://localhost");
        var otherSession = await GenerateAsync(otherSessionClient, animationId, "episode.mp4");
        var otherCookie = ReadCookie(otherSession.Response);
        using var crossSession = await GetWithCookieAsync(replicaClient, video.Link.Url, otherCookie);
        Assert.AreEqual(HttpStatusCode.NotFound, crossSession.StatusCode);

        var tamperedUrl = video.Link.Url[..^1]
                          + (video.Link.Url[^1] == 'A' ? 'B' : 'A');
        using var tampered = await GetWithCookieAsync(replicaClient, tamperedUrl, cookie);
        Assert.AreEqual(HttpStatusCode.NotFound, tampered.StatusCode);

        video.Response.Dispose();
        subtitle.Response.Dispose();
        otherSession.Response.Dispose();
    }

    private static void ConfigurePlayback(
        WebDavWebApplicationFactory factory,
        Guid animationId,
        params string[] virtualPaths)
    {
        factory.ResetState();
        foreach (var virtualPath in virtualPaths)
        {
            var physicalPath = "/physical/" + Path.GetFileName(virtualPath);
            factory.Mappings.Add(new Framework.DataRepository.FileMapping(
                Guid.NewGuid(), animationId, virtualPath, physicalPath, "local"));
            factory.FileStoreMock
                .Setup(store => store.OpenReadStreamAsync(
                    physicalPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        }

        factory.AnimationInfoRepositoryMock
            .Setup(repository => repository.FindByIdWithAnimationAsync(
                animationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDownloadedAnimation(animationId));
    }

    private static async Task<(HttpResponseMessage Response, FileLinkResultResponse Link)> GenerateAsync(
        HttpClient client,
        Guid animationId,
        string path)
    {
        var response = await client.PostAsJsonAsync(
            "/api/file/generateLink",
            new { id = animationId, path });
        response.EnsureSuccessStatusCode();
        var link = await response.Content.ReadFromJsonAsync<FileLinkResultResponse>();
        Assert.IsNotNull(link);
        return (response, link);
    }

    private static (string Name, string Value) ReadCookie(HttpResponseMessage response)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        var separator = setCookie.IndexOf('=');
        return (
            setCookie[..separator],
            setCookie[(separator + 1)..setCookie.IndexOf(';')]);
    }

    private static Task<HttpResponseMessage> GetWithCookieAsync(
        HttpClient client,
        string url,
        (string Name, string Value) cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{cookie.Name}={cookie.Value}");
        return client.SendAsync(request);
    }

    private static SecondDimensionWatcherReDive.Framework.DataRepository.AnimationInfo
        CreateDownloadedAnimation(Guid id) => new(
        id,
        "episode",
        string.Empty,
        DateTimeOffset.UtcNow,
        string.Empty,
        FileDownloadTypes.TorrentDownload,
        [],
        string.Empty,
        true,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        true,
        "local",
        "/physical",
        null,
        null,
        null,
        null,
        true,
        0);
}
