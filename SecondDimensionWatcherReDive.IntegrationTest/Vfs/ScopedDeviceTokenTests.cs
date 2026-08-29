using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using SecondDimensionWatcherReDive.IntegrationTest.Helpers;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;
using SecondDimensionWatcherReDive.WebDav;

namespace SecondDimensionWatcherReDive.IntegrationTest.Vfs;

[TestClass]
public sealed class ScopedDeviceTokenTests
{
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private WebDavWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebDavWebApplicationFactory("/Anime");
        _factory.ResetState();
        _client = _factory.CreateBasicAuthClient();
        var visible = WebDavMappingFixtures.NewMapping(
            "/Anime/episode.mkv", "/disk/visible.mkv");
        var nested = WebDavMappingFixtures.NewMapping(
            "/Anime/Sub/subtitle.srt", "/disk/subtitle.srt");
        var adjacent = WebDavMappingFixtures.NewMapping(
            "/Anime2/private.mkv", "/disk/private.mkv");
        _factory.Mappings.AddRange([visible, nested, adjacent]);
        _factory.FileStoreMock.Setup(store => store.FileInfoAsync(
                visible.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(visible, 42));
        _factory.FileStoreMock.Setup(store => store.OpenReadStreamAsync(
                visible.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task VfsRoot_IsRewritten_AndAdjacentPrefixIsHidden()
    {
        using var response = await _client.GetAsync("/api/vfs/list?path=/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<VfsEntryDto[]>(JsonOptions);
        Assert.IsNotNull(entries);
        CollectionAssert.AreEquivalent(
            new[] { "episode.mkv", "Sub" },
            entries.Select(entry => entry.Name).ToArray());
        Assert.IsFalse(entries.Any(entry => entry.Name.Contains("Anime2", StringComparison.Ordinal)));

        using var visible = await _client.GetAsync("/api/vfs/stat?path=/episode.mkv");
        using var adjacent = await _client.GetAsync("/api/vfs/stat?path=/../Anime2/private.mkv");
        Assert.AreEqual(HttpStatusCode.OK, visible.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, adjacent.StatusCode);
    }

    [TestMethod]
    public async Task WebDavRootAndHrefs_AreRewrittenToDeviceNamespace()
    {
        using var request = new HttpRequestMessage(PropFindMethod, "/webdav/");
        request.Headers.Add(WebDavConstants.Headers.Depth, "1");
        using var response = await _client.SendAsync(request);

        Assert.AreEqual((HttpStatusCode)207, response.StatusCode);
        var multiStatus = await WebDavXmlAssertions.ReadMultiStatusAsync(response);
        var hrefs = multiStatus.Responses.Select(item => item.Href).ToArray();
        CollectionAssert.Contains(hrefs, "/webdav/");
        CollectionAssert.Contains(hrefs, "/webdav/episode.mkv");
        CollectionAssert.Contains(hrefs, "/webdav/Sub/");
        Assert.IsFalse(hrefs.Any(href => href.Contains("/Anime", StringComparison.Ordinal)));

        using var file = await _client.GetAsync("/webdav/episode.mkv");
        Assert.AreEqual(HttpStatusCode.OK, file.StatusCode);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await file.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task RevokedDeviceToken_IsRejectedImmediately()
    {
        Assert.IsTrue(await _factory.DeviceTokenRepository.RevokeByIdAsync(
            _factory.DeviceTokenRepository.TokenId,
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        using var response = await _client.GetAsync("/api/vfs/stat?path=/episode.mkv");
        using var webDav = await SendRootPropFindAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, webDav.StatusCode);
    }

    [TestMethod]
    public async Task ExpiredDeviceToken_IsRejectedByVfsAndWebDav()
    {
        _factory.DeviceTokenRepository.Expire();

        using var vfs = await _client.GetAsync("/api/vfs/stat?path=/episode.mkv");
        using var webDav = await SendRootPropFindAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, vfs.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, webDav.StatusCode);
    }

    [TestMethod]
    public async Task NonReadDeviceToken_IsRejectedByVfsAndWebDav()
    {
        _factory.DeviceTokenRepository.SetScope("write");

        using var vfs = await _client.GetAsync("/api/vfs/stat?path=/episode.mkv");
        using var webDav = await SendRootPropFindAsync();

        Assert.AreEqual(HttpStatusCode.Unauthorized, vfs.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, webDav.StatusCode);
    }

    private async Task<HttpResponseMessage> SendRootPropFindAsync()
    {
        using var request = new HttpRequestMessage(PropFindMethod, "/webdav/");
        request.Headers.Add(WebDavConstants.Headers.Depth, "0");
        return await _client.SendAsync(request);
    }

    private sealed record VfsEntryDto(
        string Name,
        bool IsDirectory,
        long? Size,
        DateTimeOffset? LastModifiedUtc);
}
