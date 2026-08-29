using System.Net;
using System.Text;
using SecondDimensionWatcherReDive.IntegrationTest.Helpers;
using SecondDimensionWatcherReDive.IntegrationTest.TestData;
using SecondDimensionWatcherReDive.WebDav;
using Moq;

namespace SecondDimensionWatcherReDive.IntegrationTest.Methods;

[TestClass]
public sealed class WebDavPropFindTests
{
    // Most PROPFIND scenarios are exercised through WebDav.Client in WebDavClientLibraryTests
    // and WebDavClientAdvancedTests. This class keeps tests that require sending raw XML bodies
    // (which WebDav.Client's PropfindParameters cannot express) and the RootEntries optimization
    // assertion that probes the FakeFileMappingRepository directly.
    private static readonly HttpMethod PropFindMethod = new("PROPFIND");

    private WebDavWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new WebDavWebApplicationFactory();
        _factory.ResetState();
        _client = _factory.CreateBasicAuthClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private void SeedDefaultMappings()
    {
        var f1 = WebDavMappingFixtures.NewMapping("/anime-a/file1.mkv", "/disk/file1.mkv");
        var sub = WebDavMappingFixtures.NewMapping("/anime-a/sub/extra.srt", "/disk/extra.srt");
        var f2 = WebDavMappingFixtures.NewMapping("/anime-b/file2.mkv", "/disk/file2.mkv");
        _factory.Mappings.AddRange(new[] { f1, sub, f2 });

        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(f1.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(f1, 1024L));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(sub.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(sub, 256L));
        _factory.FileStoreMock
            .Setup(s => s.FileInfoAsync(f2.PhysicalPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebDavMappingFixtures.InfoFor(f2, 2048L));
    }

    private static HttpRequestMessage NewPropFind(string path, string? depth = "0", string? body = null)
    {
        var req = new HttpRequestMessage(PropFindMethod, path);
        if (depth is not null) req.Headers.Add(WebDavConstants.Headers.Depth, depth);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/xml");
        return req;
    }

    [TestMethod]
    public async Task Depth_Infinity_On_Collection_Returns_403()
    {
        SeedDefaultMappings();
        using var req = NewPropFind("/webdav/", depth: "infinity");

        using var response = await _client.SendAsync(req);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Allprop_Body_Returns_Standard_Properties()
    {
        SeedDefaultMappings();
        const string body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:allprop/></d:propfind>";
        using var req = NewPropFind("/webdav/anime-a/file1.mkv", depth: "0", body: body);

        using var response = await _client.SendAsync(req);

        Assert.AreEqual((HttpStatusCode)207, response.StatusCode);
        var multi = await WebDavXmlAssertions.ReadMultiStatusAsync(response);
        var prop = multi.Responses[0].PropStats[0].Prop;
        Assert.IsNotNull(prop.GetContentLength);
        Assert.IsNotNull(prop.GetContentType);
        Assert.IsNotNull(prop.ResourceType);
    }

    [TestMethod]
    public async Task Invalid_Xml_Body_Falls_Back_To_Allprop()
    {
        // WebDavController.TryReadPropFindRequestAsync swallows deserialization failures
        // and returns null, which is treated as allprop.
        SeedDefaultMappings();
        using var req = NewPropFind("/webdav/anime-a/file1.mkv", depth: "0", body: "<<not-xml>>");

        using var response = await _client.SendAsync(req);

        Assert.AreEqual((HttpStatusCode)207, response.StatusCode);
    }

    [TestMethod]
    public async Task Root_Depth1_Should_Not_Load_Every_Mapping()
    {
        // Listing the root must use the indexed immediate-child query, not an
        // unbounded prefix scan that loads every FileMapping for "/".
        SeedDefaultMappings();
        using var req = NewPropFind("/webdav/", depth: "1");

        using var response = await _client.SendAsync(req);

        Assert.AreEqual((HttpStatusCode)207, response.StatusCode);
        CollectionAssert.Contains(_factory.MappingRepository.ImmediateChildrenCalls, "/");
        CollectionAssert.DoesNotContain(_factory.MappingRepository.PrefixCalls, "/");
    }
}
