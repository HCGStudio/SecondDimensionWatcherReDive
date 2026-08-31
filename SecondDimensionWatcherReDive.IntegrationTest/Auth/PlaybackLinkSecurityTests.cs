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
