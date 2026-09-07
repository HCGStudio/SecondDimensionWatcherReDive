using System.Net;

namespace SecondDimensionWatcherReDive.IntegrationTest.Health;

[TestClass]
public sealed class ReadinessTests
{
    [TestMethod]
    public async Task LiveEndpoint_IsAvailableAfterStartupMigrationGateCompletes()
    {
        using var factory = new WebDavWebApplicationFactory();
        using var client = factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
