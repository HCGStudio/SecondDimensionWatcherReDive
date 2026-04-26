using System.Net;

namespace SecondDimensionWatcherReDive.IntegrationTest.Vfs;

[TestClass]
public sealed class VfsAuthTests
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
    public async Task Missing_Authorization_Returns_401()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/api/vfs/list?path=/");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        StringAssert.Contains(challenge, "Basic");
    }

    [TestMethod]
    public async Task Wrong_Credentials_Returns_401()
    {
        using var client = _factory.CreateBasicAuthClient(pass: "not-the-password");

        using var response = await client.GetAsync("/api/vfs/list?path=/");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Jwt_Cannot_Reach_Vfs()
    {
        using var client = _factory.CreateJwtClient();

        using var response = await client.GetAsync("/api/vfs/list?path=/");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Valid_Basic_Credentials_Pass()
    {
        using var client = _factory.CreateBasicAuthClient();

        using var response = await client.GetAsync("/api/vfs/stat?path=/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
