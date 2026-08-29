using System.Net;
using System.Text.Json;

namespace SecondDimensionWatcherReDive.IntegrationTest.Health;

[TestClass]
public sealed class HealthEndpointTests
{
    private WebDavWebApplicationFactory _factory = null!;

    [TestInitialize]
    public void Setup() => _factory = new WebDavWebApplicationFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task Liveness_HasNoExternalChecks_WhenReadinessDependencyFails()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
        using var liveBody = await JsonDocument.ParseAsync(
            await live.Content.ReadAsStreamAsync());
        Assert.AreEqual(0, liveBody.RootElement.GetProperty("checks").EnumerateObject().Count());
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [TestMethod]
    public async Task Metrics_ArePublicAndDoNotExposeRawPathLabels()
    {
        using var client = _factory.CreateUnauthenticatedClient();
        using var _ = await client.GetAsync("/health/live?private=value");

        using var response = await client.GetAsync("/metrics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(body, "target_info");
        Assert.IsFalse(body.Contains("url.path", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains("url_path", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(body.Contains("private=value", StringComparison.Ordinal));
    }
}
