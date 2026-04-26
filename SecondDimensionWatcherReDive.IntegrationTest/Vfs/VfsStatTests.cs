using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;

namespace SecondDimensionWatcherReDive.IntegrationTest.Vfs;

[TestClass]
public sealed class VfsStatTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private WebDavWebApplicationFactory _factory = null!;
    private HttpClient _httpClient = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebDavWebApplicationFactory();
        _factory.ResetState();
        _httpClient = _factory.CreateBasicAuthClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _httpClient.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Stat_Root_Returns_Directory()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/stat?path=/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<VfsEntryDto>(JsonOptions);
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry!.IsDirectory);
        Assert.AreEqual(string.Empty, entry.Name);
        Assert.IsNull(entry.Size);
    }

    [TestMethod]
    public async Task Stat_Empty_Path_Treated_As_Root()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/stat");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<VfsEntryDto>(JsonOptions);
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry!.IsDirectory);
    }

    [TestMethod]
    public async Task Stat_File_Returns_Size_And_LastModified()
    {
        var mapping = WebDavMappingFixtures.NewMapping("/anime-a/sub/ep01.mkv", "/disk/ep01.mkv");
        _factory.Mappings.Add(mapping);
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(mapping, 1024));

        using var response = await _httpClient.GetAsync("/api/vfs/stat?path=/anime-a/sub/ep01.mkv");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<VfsEntryDto>(JsonOptions);
        Assert.IsNotNull(entry);
        Assert.IsFalse(entry!.IsDirectory);
        Assert.AreEqual("ep01.mkv", entry.Name);
        Assert.AreEqual(1024L, entry.Size);
        Assert.AreEqual(WebDavMappingFixtures.FixedModified, entry.LastModifiedUtc);
    }

    [TestMethod]
    public async Task Stat_Synthetic_Directory_Returns_Directory_Without_Size()
    {
        var mapping = WebDavMappingFixtures.NewMapping("/anime-a/sub/ep01.mkv", "/disk/ep01.mkv");
        _factory.Mappings.Add(mapping);
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(mapping, 1024));

        using var response = await _httpClient.GetAsync("/api/vfs/stat?path=/anime-a/sub");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<VfsEntryDto>(JsonOptions);
        Assert.IsNotNull(entry);
        Assert.IsTrue(entry!.IsDirectory);
        Assert.AreEqual("sub", entry.Name);
        Assert.IsNull(entry.Size);
        Assert.IsNull(entry.LastModifiedUtc);
    }

    [TestMethod]
    public async Task Stat_Missing_Path_Returns_404()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/stat?path=/does/not/exist");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Stat_Path_Without_Leading_Slash_Returns_400()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/stat?path=anime-a");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Stat_Path_Traversal_Returns_400()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/stat?path=/anime-a/../etc");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record VfsEntryDto(string Name, bool IsDirectory, long? Size, DateTimeOffset? LastModifiedUtc);
}
