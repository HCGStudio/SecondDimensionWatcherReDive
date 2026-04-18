namespace SecondDimensionWatcherReDive.IntegrationTest.Methods;

[TestClass]
public sealed class WebDavOptionsTests
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
    public async Task Options_Returns_DavHeaders_And_Allow()
    {
        using var client = _factory.CreateBasicAuthClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/webdav/");
        using var response = await client.SendAsync(request);

        Assert.IsTrue((int)response.StatusCode is >= 200 and < 300,
            $"OPTIONS expected 2xx, got {(int)response.StatusCode}");
        Assert.IsTrue(response.Headers.TryGetValues("DAV", out var dav));
        CollectionAssert.Contains(dav!.ToList(), "1");

        Assert.IsTrue(response.Headers.TryGetValues("Allow", out var allow) ||
                      response.Content.Headers.TryGetValues("Allow", out allow));
        var allowValue = string.Join(",", allow!);
        StringAssert.Contains(allowValue, "OPTIONS");
        StringAssert.Contains(allowValue, "PROPFIND");
        StringAssert.Contains(allowValue, "HEAD");
        StringAssert.Contains(allowValue, "GET");
    }
}
