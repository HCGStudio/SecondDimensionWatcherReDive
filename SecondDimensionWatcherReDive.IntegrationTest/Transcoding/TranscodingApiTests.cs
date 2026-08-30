using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecondDimensionWatcherReDive.IntegrationTest.Transcoding;

[TestClass]
public sealed class TranscodingApiTests
{
    private WebDavWebApplicationFactory _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebDavWebApplicationFactory();
        _factory.ResetState();
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task PrepareRequiresJwtButTokenizedHlsResourcesAreAnonymous()
    {
        using var anonymous = _factory.CreateUnauthenticatedClient();
        using var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/transcoding/prepare",
            new { id = Guid.NewGuid(), path = "episode.mkv", quality = "auto" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var jwt = _factory.CreateJwtClient();
        using var prepared = await jwt.PostAsJsonAsync(
            "/api/transcoding/prepare",
            new { id = Guid.NewGuid(), path = "episode.mkv", quality = "auto" });
        Assert.AreEqual(HttpStatusCode.OK, prepared.StatusCode);
        using var payload = JsonDocument.Parse(await prepared.Content.ReadAsStringAsync());
        var playbackUrl = payload.RootElement.GetProperty("playbackUrl").GetString();
        var statusUrl = payload.RootElement.GetProperty("statusUrl").GetString();
        Assert.IsNotNull(playbackUrl);
        Assert.IsNotNull(statusUrl);

        using var status = await anonymous.GetAsync(statusUrl);
        Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
        using var playlist = await anonymous.GetAsync(playbackUrl);
        Assert.AreEqual(HttpStatusCode.OK, playlist.StatusCode);
        Assert.AreEqual("application/vnd.apple.mpegurl", playlist.Content.Headers.ContentType?.MediaType);
        var segmentUrl = (await playlist.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => !line.StartsWith('#'));
        StringAssert.Contains(segmentUrl, _factory.TranscodingService.Token);
        using var segment = await anonymous.GetAsync(segmentUrl);
        Assert.AreEqual(HttpStatusCode.OK, segment.StatusCode);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await segment.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task InvalidSessionTokenIsRejectedAndMetricsRequireJwt()
    {
        using var anonymous = _factory.CreateUnauthenticatedClient();
        using var invalid = await anonymous.GetAsync(
            $"/api/transcoding/sessions/{_factory.TranscodingService.SessionId}?token=wrong");
        Assert.AreEqual(HttpStatusCode.NotFound, invalid.StatusCode);
        using var unauthorizedMetrics = await anonymous.GetAsync("/api/transcoding/metrics");
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorizedMetrics.StatusCode);

        using var jwt = _factory.CreateJwtClient();
        using var metrics = await jwt.GetAsync("/api/transcoding/metrics");
        Assert.AreEqual(HttpStatusCode.OK, metrics.StatusCode);
        using var payload = JsonDocument.Parse(await metrics.Content.ReadAsStringAsync());
        Assert.AreEqual(0.2, payload.RootElement.GetProperty("failureRate").GetDouble());
    }
}
