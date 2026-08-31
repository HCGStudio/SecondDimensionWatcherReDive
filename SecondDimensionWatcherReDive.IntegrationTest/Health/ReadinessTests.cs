using System.Net;

namespace SecondDimensionWatcherReDive.IntegrationTest.Health;

[TestClass]
public sealed class ReadinessTests
{
    [TestMethod]
    public async Task ReadyEndpoint_IsAvailableAfterStartupMigrationGateCompletes()
    {
        using var factory = new WebDavWebApplicationFactory();
        using var client = factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("ready", await response.Content.ReadAsStringAsync());
    }
}
