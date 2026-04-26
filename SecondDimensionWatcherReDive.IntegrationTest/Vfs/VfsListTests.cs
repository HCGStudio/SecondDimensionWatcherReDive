using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;

namespace SecondDimensionWatcherReDive.IntegrationTest.Vfs;

[TestClass]
public sealed class VfsListTests
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
    public async Task List_Root_Returns_Top_Level_Directories()
    {
        _factory.Mappings.Add(WebDavMappingFixtures.NewMapping("/anime-a/sub/ep01.mkv", "/disk/a-ep01.mkv"));
        _factory.Mappings.Add(WebDavMappingFixtures.NewMapping("/anime-b/sub/ep01.mkv", "/disk/b-ep01.mkv"));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string p, CancellationToken _) =>
                new FileStoreInfo(false, p, Path.GetFileName(p), 100, WebDavMappingFixtures.FixedModified));

        using var response = await _httpClient.GetAsync("/api/vfs/list?path=/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<VfsEntryDto[]>(JsonOptions);
        Assert.IsNotNull(entries);
        var names = entries!.Select(e => e.Name).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(new[] { "anime-a", "anime-b" }, names);
        Assert.IsTrue(entries.All(e => e.IsDirectory));
    }

    [TestMethod]
    public async Task List_Directory_Returns_Children_With_File_Metadata()
    {
        var ep1 = WebDavMappingFixtures.NewMapping("/anime-a/sub/ep01.mkv", "/disk/ep01.mkv");
        var ep2 = WebDavMappingFixtures.NewMapping("/anime-a/sub/ep02.mkv", "/disk/ep02.mkv");
        _factory.Mappings.Add(ep1);
        _factory.Mappings.Add(ep2);
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(ep1.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(ep1, 1024));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(ep2.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(ep2, 2048));

        using var response = await _httpClient.GetAsync("/api/vfs/list?path=/anime-a/sub");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<VfsEntryDto[]>(JsonOptions);
        Assert.IsNotNull(entries);
        var byName = entries!.OrderBy(e => e.Name).ToArray();
        Assert.AreEqual(2, byName.Length);
        Assert.AreEqual("ep01.mkv", byName[0].Name);
        Assert.IsFalse(byName[0].IsDirectory);
        Assert.AreEqual(1024L, byName[0].Size);
        Assert.AreEqual(WebDavMappingFixtures.FixedModified, byName[0].LastModifiedUtc);
        Assert.AreEqual("ep02.mkv", byName[1].Name);
        Assert.AreEqual(2048L, byName[1].Size);
    }

    [TestMethod]
    public async Task List_Mixed_Children_Distinguishes_Files_And_Directories()
    {
        var file = WebDavMappingFixtures.NewMapping("/anime-a/readme.txt", "/disk/readme.txt");
        var nested = WebDavMappingFixtures.NewMapping("/anime-a/sub/ep01.mkv", "/disk/ep01.mkv");
        _factory.Mappings.Add(file);
        _factory.Mappings.Add(nested);
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(file.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(file, 7));

        using var response = await _httpClient.GetAsync("/api/vfs/list?path=/anime-a");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var entries = await response.Content.ReadFromJsonAsync<VfsEntryDto[]>(JsonOptions);
        Assert.IsNotNull(entries);
        var byName = entries!.OrderBy(e => e.Name).ToArray();
        Assert.AreEqual(2, byName.Length);
        Assert.AreEqual("readme.txt", byName[0].Name);
        Assert.IsFalse(byName[0].IsDirectory);
        Assert.AreEqual(7L, byName[0].Size);
        Assert.AreEqual("sub", byName[1].Name);
        Assert.IsTrue(byName[1].IsDirectory);
        Assert.IsNull(byName[1].Size);
    }

    [TestMethod]
    public async Task List_File_Path_Returns_400()
    {
        var mapping = WebDavMappingFixtures.NewMapping("/anime-a/ep01.mkv", "/disk/ep01.mkv");
        _factory.Mappings.Add(mapping);
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(mapping, 100));

        using var response = await _httpClient.GetAsync("/api/vfs/list?path=/anime-a/ep01.mkv");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task List_Missing_Path_Returns_404()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/list?path=/no/such/dir");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record VfsEntryDto(string Name, bool IsDirectory, long? Size, DateTimeOffset? LastModifiedUtc);
}
