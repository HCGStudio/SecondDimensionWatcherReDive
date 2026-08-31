using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Moq;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;

namespace SecondDimensionWatcherReDive.IntegrationTest.Vfs;

[TestClass]
public sealed class VfsReadTests
{
    private const string FileBody = "hello-world!"; // 12 bytes
    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes(FileBody);

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

    private void SeedFile()
    {
        var mapping = WebDavMappingFixtures.NewMapping("/anime-a/ep01.mkv", "/disk/ep01.mkv");
        _factory.Mappings.Add(mapping);

        _factory.FileStoreMock
            .Setup(s => s.OpenReadStreamAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(FileBytes, writable: false));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, mapping.PhysicalPath, "ep01.mkv",
                FileBytes.LongLength, WebDavMappingFixtures.FixedModified));
    }

    [TestMethod]
    public async Task Read_Full_File_Returns_Bytes()
    {
        SeedFile();

        using var response = await _httpClient.GetAsync("/api/vfs/read?path=/anime-a/ep01.mkv");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(FileBytes, bytes);
        Assert.AreEqual(FileBytes.LongLength, response.Content.Headers.ContentLength);
    }

    [TestMethod]
    public async Task Read_With_Range_Returns_Partial_Content()
    {
        SeedFile();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/vfs/read?path=/anime-a/ep01.mkv");
        request.Headers.Range = new RangeHeaderValue(2, null);
        using var response = await _httpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.PartialContent, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(FileBytes.Skip(2).ToArray(), bytes);
    }

    [TestMethod]
    public async Task Read_Directory_Returns_404()
    {
        SeedFile();

        using var response = await _httpClient.GetAsync("/api/vfs/read?path=/anime-a");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Read_Root_Returns_404()
    {
        SeedFile();

        using var response = await _httpClient.GetAsync("/api/vfs/read?path=/");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Read_Missing_File_Returns_404()
    {
        using var response = await _httpClient.GetAsync("/api/vfs/read?path=/does-not-exist.mkv");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
