using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Moq;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;
using WebDav;

namespace SecondDimensionWatcherReDive.IntegrationTest.Methods;

[TestClass]
public sealed class WebDavClientAdvancedTests
{
    private const string FileBody = "advanced-webdav-payload";
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

    private void SeedFile(string virtualPath = "/anime-a/file1.mkv", string physicalPath = "/disk/file1.mkv")
    {
        var mapping = WebDavMappingFixtures.NewMapping(virtualPath, physicalPath);
        _factory.Mappings.Add(mapping);

        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(physicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileStoreInfo(false, physicalPath, Path.GetFileName(virtualPath),
                FileBytes.LongLength, WebDavMappingFixtures.FixedModified));
        _factory.FileStoreMock
            .Setup(s => s.OpenReadStreamAsync(physicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(FileBytes, writable: false));
    }

    [TestMethod]
    public async Task Lock_Returns_405_MethodNotAllowed()
    {
        var response = await _client.Lock("/webdav/anime-a/file1.mkv");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task Unlock_Returns_405_MethodNotAllowed()
    {
        var response = await _client.Unlock("/webdav/anime-a/file1.mkv", "opaquelocktoken:does-not-matter");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task Proppatch_Returns_405_MethodNotAllowed()
    {
        SeedFile();

        var response = await _client.Proppatch("/webdav/anime-a/file1.mkv",
            new ProppatchParameters
            {
                PropertiesToSet = new Dictionary<XName, string>
                {
                    [XName.Get("displayname", "DAV:")] = "renamed.mkv"
                }
            });

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    [TestMethod]
    public async Task GetProcessedFile_Returns_Body_With_Translation()
    {
        SeedFile();

        using var response = await _client.GetProcessedFile("/webdav/anime-a/file1.mkv");

        Assert.IsTrue(response.IsSuccessful, $"GetProcessedFile failed: {response.Description}");
        using var ms = new MemoryStream();
        await response.Stream.CopyToAsync(ms);
        CollectionAssert.AreEqual(FileBytes, ms.ToArray());
    }

    [TestMethod]
    public async Task GetRawFile_With_RangeHeader_Returns_206_PartialContent()
    {
        SeedFile();

        var range = new RangeHeaderValue(0, 7);
        var parameters = new GetFileParameters
        {
            Headers = new[] { new KeyValuePair<string, string>("Range", range.ToString()) }
        };

        using var response = await _client.GetRawFile("/webdav/anime-a/file1.mkv", parameters);

        Assert.AreEqual(206, response.StatusCode);
        using var ms = new MemoryStream();
        await response.Stream.CopyToAsync(ms);
        CollectionAssert.AreEqual(FileBytes.Take(8).ToArray(), ms.ToArray());
    }

    [TestMethod]
    public async Task Propfind_With_RequestedProperties_Returns_Only_Subset()
    {
        SeedFile();

        var parameters = new PropfindParameters
        {
            ApplyTo = ApplyTo.Propfind.ResourceOnly,
            RequestType = PropfindRequestType.NamedProperties,
            CustomProperties = new[] { XName.Get("getcontentlength", "DAV:") }
        };

        var response = await _client.Propfind("/webdav/anime-a/file1.mkv", parameters);

        Assert.IsTrue(response.IsSuccessful, response.Description);
        var resource = response.Resources.Single();
        Assert.AreEqual(FileBytes.LongLength, resource.ContentLength);
        // ContentType, ETag, LastModified were not requested → controller filter drops them.
        Assert.IsNull(resource.ContentType);
        Assert.IsNull(resource.ETag);
        Assert.IsNull(resource.LastModifiedDate);
    }

    [TestMethod]
    public async Task Propfind_PropName_Advertises_Property_Names_As_Empty_Elements()
    {
        SeedFile();

        // WebDav.Client doesn't expose PropName directly, so we send the raw XML.
        var raw = await SendRawPropFindAsync("/webdav/anime-a/file1.mkv", "0",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:propname/></d:propfind>");

        Assert.AreEqual(207, (int)raw.StatusCode);
        var multi = await Helpers.WebDavXmlAssertions.ReadMultiStatusAsync(raw);
        var prop = multi.Responses[0].PropStats[0].Prop;
        // Names are advertised as empty elements (string.Empty round-trips through XmlSerializer).
        Assert.AreEqual(string.Empty, prop.GetContentLength);
        Assert.AreEqual(string.Empty, prop.GetContentType);
        Assert.AreEqual(string.Empty, prop.GetETag);
        Assert.AreEqual(string.Empty, prop.GetLastModified);
        Assert.AreEqual(string.Empty, prop.CreationDate);
        Assert.IsNotNull(prop.ResourceType);
        Assert.IsFalse(prop.ResourceType!.IsCollection);
        Assert.IsNotNull(prop.LockDiscovery);
        Assert.IsNotNull(prop.SupportedLock);
    }

    [TestMethod]
    public async Task Propfind_Path_With_SpecialCharacters_Escapes_Href()
    {
        const string virtualPath = "/Anime/A B+C/ep 01.mkv";
        const string physicalPath = "/disk/escaped";
        SeedFile(virtualPath, physicalPath);

        var raw = await SendRawPropFindAsync("/webdav/Anime/A%20B%2BC/ep%2001.mkv", "0", body: null);

        Assert.AreEqual(207, (int)raw.StatusCode);
        var multi = await Helpers.WebDavXmlAssertions.ReadMultiStatusAsync(raw);
        Assert.AreEqual("/webdav/Anime/A%20B%2BC/ep%2001.mkv", multi.Responses[0].Href);
    }

    [TestMethod]
    public async Task GetRawFile_On_Synthetic_Directory_Returns_405()
    {
        SeedFile();

        using var response = await _client.GetRawFile("/webdav/anime-a/");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(405, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendRawPropFindAsync(string url, string depth, string? body)
    {
        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), url);
        req.Headers.Add("Depth", depth);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/xml");
        return await _httpClient.SendAsync(req);
    }

    [TestMethod]
    public async Task Propfind_Unsupported_Named_Property_Reported_In_404_PropStat()
    {
        SeedFile();

        // Mix of: known DAV property (getcontentlength), known vendor extension (quota-available-bytes),
        // and a totally unknown vendor property (vendor:unknown-thing).
        const string body =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<d:propfind xmlns:d=\"DAV:\" xmlns:v=\"http://example.com/vendor\">" +
            "<d:prop><d:getcontentlength/><d:quota-available-bytes/><v:unknown-thing/></d:prop>" +
            "</d:propfind>";

        var raw = await SendRawPropFindAsync("/webdav/anime-a/file1.mkv", "0", body);

        Assert.AreEqual(207, (int)raw.StatusCode);
        var multi = await Helpers.WebDavXmlAssertions.ReadMultiStatusAsync(raw);
        var response = multi.Responses[0];
        Assert.AreEqual(2, response.PropStats.Count, "Expected 200 + 404 propstat pair");

        var ok = response.PropStats.Single(ps => ps.Status.Contains("200"));
        Assert.IsNotNull(ok.Prop.GetContentLength);

        var notFound = response.PropStats.Single(ps => ps.Status.Contains("404"));
        Assert.AreEqual(1, notFound.Prop.Extensions.Count);
        Assert.AreEqual("unknown-thing", notFound.Prop.Extensions[0].LocalName);
        Assert.AreEqual("http://example.com/vendor", notFound.Prop.Extensions[0].NamespaceURI);
    }

    [TestMethod]
    public async Task Propfind_Recognised_Named_Property_Does_Not_Produce_404_PropStat()
    {
        SeedFile();

        const string body =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/></d:prop></d:propfind>";

        var raw = await SendRawPropFindAsync("/webdav/anime-a/file1.mkv", "0", body);

        Assert.AreEqual(207, (int)raw.StatusCode);
        var multi = await Helpers.WebDavXmlAssertions.ReadMultiStatusAsync(raw);
        Assert.AreEqual(1, multi.Responses[0].PropStats.Count);
        StringAssert.Contains(multi.Responses[0].PropStats[0].Status, "200");
    }

    [TestMethod]
    public async Task Propfind_With_Chunked_Body_Is_Honoured()
    {
        SeedFile();

        // Send PROPFIND with Transfer-Encoding: chunked (no Content-Length). Previously the
        // controller short-circuited on null ContentLength and silently degraded to allprop;
        // here we ask for getcontentlength only and assert other properties are filtered out.
        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), "/webdav/anime-a/file1.mkv");
        req.Headers.Add("Depth", "0");
        req.Headers.TransferEncodingChunked = true;
        req.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/></d:prop></d:propfind>")));
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml")
        {
            CharSet = "utf-8"
        };

        using var response = await _httpClient.SendAsync(req);

        Assert.AreEqual(207, (int)response.StatusCode);
        var multi = await Helpers.WebDavXmlAssertions.ReadMultiStatusAsync(response);
        var prop = multi.Responses[0].PropStats[0].Prop;
        Assert.IsNotNull(prop.GetContentLength);
        // If the body had been ignored (allprop fallback) we'd also see content-type/etag.
        Assert.IsNull(prop.GetContentType);
        Assert.IsNull(prop.GetETag);
    }
}
