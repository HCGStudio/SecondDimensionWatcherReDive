using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Moq;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;
using WebDav;

namespace SecondDimensionWatcherReDive.IntegrationTest.Methods;

[TestClass]
public sealed class WebDavGetHeadTests
{
    // GET happy-path / 404 / 405 / range-from-zero are exercised in WebDavClientLibraryTests
    // and WebDavClientAdvancedTests. This class keeps tests for HEAD (no WebDav.Client primitive)
    // and the remaining range edge cases, expressed via WebDav.Client where possible.
    private const string FileBody = "hello-world!"; // 12 bytes
    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes(FileBody);

    private WebDavWebApplicationFactory _factory = null!;
    private HttpClient _httpClient = null!;
    private WebDavClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebDavWebApplicationFactory();
        _factory.ResetState();
        _httpClient = _factory.CreateBasicAuthClient();
        _client = new WebDavClient(_httpClient);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _factory.Dispose();
    }

    private void SeedFile()
    {
        var mapping = WebDavMappingFixtures.NewMapping("/anime-a/file1.mkv", "/disk/file1.mkv");
        _factory.Mappings.Add(mapping);

        _factory.FileStoreMock
            .Setup(s => s.OpenReadStreamAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(FileBytes, writable: false));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(mapping.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, mapping.PhysicalPath, "file1.mkv",
                FileBytes.LongLength, WebDavMappingFixtures.FixedModified));
    }

    [TestMethod]
    public async Task Head_File_Returns_Headers_Without_Body()
    {
        // WebDav.Client has no HEAD primitive, so use the underlying HttpClient.
        SeedFile();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/webdav/anime-a/file1.mkv");
        using var response = await _httpClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(FileBytes.LongLength, response.Content.Headers.ContentLength);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.AreEqual(0, bytes.Length);
    }

    [TestMethod]
    public async Task GetRawFile_With_Range_From_Offset_Returns_Tail_Slice()
    {
        SeedFile();

        var parameters = new GetFileParameters
        {
            Headers = new[] { new KeyValuePair<string, string>("Range", new RangeHeaderValue(2, null).ToString()) }
        };

        using var response = await _client.GetRawFile("/webdav/anime-a/file1.mkv", parameters);

        Assert.AreEqual(206, response.StatusCode);
        using var ms = new MemoryStream();
        await response.Stream.CopyToAsync(ms);
        CollectionAssert.AreEqual(FileBytes.Skip(2).ToArray(), ms.ToArray());
    }

    [TestMethod]
    public async Task GetRawFile_With_OutOfRange_Range_Returns_416()
    {
        SeedFile();

        var parameters = new GetFileParameters
        {
            Headers = new[] { new KeyValuePair<string, string>("Range", new RangeHeaderValue(1000, 2000).ToString()) }
        };

        using var response = await _client.GetRawFile("/webdav/anime-a/file1.mkv", parameters);

        Assert.AreEqual((int)HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
    }
}
