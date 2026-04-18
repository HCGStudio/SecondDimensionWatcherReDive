using System.Net;
using WebDav;

namespace SecondDimensionWatcherReDive.IntegrationTest.Auth;

[TestClass]
public sealed class WebDavAuthTests
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
    public async Task Missing_Authorization_Returns_401_With_BasicChallenge()
    {
        // Use raw HttpClient — WebDav.Client doesn't expose response headers cleanly,
        // and we want to assert WWW-Authenticate.
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/webdav/");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        StringAssert.Contains(challenge, "Basic");
        StringAssert.Contains(challenge, "realm=\"SecondDimensionWatcher WebDAV\"");
        StringAssert.Contains(challenge, "charset=\"UTF-8\"");
    }

    [TestMethod]
    public async Task Wrong_Password_Returns_401()
    {
        using var http = _factory.CreateBasicAuthClient(pass: "not-the-password");
        using var client = new WebDavClient(http);

        var response = await client.Propfind("/webdav/");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(401, response.StatusCode);
    }

    [TestMethod]
    public async Task Wrong_Username_Returns_401()
    {
        using var http = _factory.CreateBasicAuthClient(user: "not-sdwuser");
        using var client = new WebDavClient(http);

        var response = await client.Propfind("/webdav/");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(401, response.StatusCode);
    }

    [TestMethod]
    public async Task Valid_BasicCredentials_Pass_Authorization()
    {
        using var http = _factory.CreateBasicAuthClient();
        using var client = new WebDavClient(http);

        // Propfind with default parameters → server returns 207 (authorized).
        var response = await client.Propfind("/webdav/");

        Assert.IsTrue(response.IsSuccessful, $"Propfind expected 2xx, got {response.StatusCode}");
        Assert.AreEqual(207, response.StatusCode);
    }

    [TestMethod]
    public async Task Jwt_Token_Cannot_Reach_WebDav()
    {
        using var http = _factory.CreateJwtClient();
        using var client = new WebDavClient(http);

        var response = await client.Propfind("/webdav/");

        Assert.IsFalse(response.IsSuccessful);
        Assert.AreEqual(401, response.StatusCode);
    }
}
