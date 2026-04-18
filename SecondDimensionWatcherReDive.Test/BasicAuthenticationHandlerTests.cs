using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Auth;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class BasicAuthenticationHandlerTests
{
    private const string ValidPassword = "correct-horse";
    private string _hash = null!;

    [TestInitialize]
    public void Setup()
    {
        _hash = BCrypt.Net.BCrypt.HashPassword(ValidPassword);
    }

    private async Task<(BasicAuthenticationHandler handler, DefaultHttpContext httpContext)> CreateHandlerAsync(
        string? authorization,
        string? storedHash)
    {
        var settings = new Dictionary<string, string?>();
        if (storedHash is not null) settings["Password:Value"] = storedHash;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Setup(m => m.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var handler = new BasicAuthenticationHandler(
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            configuration);

        var httpContext = new DefaultHttpContext();
        if (authorization is not null) httpContext.Request.Headers["Authorization"] = authorization;

        var scheme = new AuthenticationScheme(BasicAuthenticationHandler.SchemeName, null, typeof(BasicAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);
        return (handler, httpContext);
    }

    private static string BasicHeader(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    [TestMethod]
    public async Task NoAuthorizationHeader_ReturnsNoResult()
    {
        var (handler, _) = await CreateHandlerAsync(authorization: null, _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Failure is not null, "Expected NoResult, not failure");
        Assert.IsTrue(result.None);
    }

    [TestMethod]
    public async Task WrongScheme_ReturnsNoResult()
    {
        var (handler, _) = await CreateHandlerAsync("Bearer abc", _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsTrue(result.None);
    }

    [TestMethod]
    public async Task MalformedBase64_Fails()
    {
        var (handler, _) = await CreateHandlerAsync("Basic !!!not-base64!!!", _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.Failure);
    }

    [TestMethod]
    public async Task MissingColonSeparator_Fails()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("noseparator"));
        var (handler, _) = await CreateHandlerAsync($"Basic {encoded}", _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task WrongUsername_Fails()
    {
        var (handler, _) = await CreateHandlerAsync(BasicHeader("admin", ValidPassword), _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task WrongPassword_Fails()
    {
        var (handler, _) = await CreateHandlerAsync(BasicHeader("sdwuser", "wrong"), _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task UnconfiguredPassword_Fails()
    {
        var (handler, _) = await CreateHandlerAsync(BasicHeader("sdwuser", ValidPassword), storedHash: null);
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ValidCredentials_Succeed()
    {
        var (handler, _) = await CreateHandlerAsync(BasicHeader("sdwuser", ValidPassword), _hash);
        var result = await handler.AuthenticateAsync();
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("sdwuser", result.Principal!.Identity!.Name);
    }

    [TestMethod]
    public async Task Challenge_SetsBasicWwwAuthenticate()
    {
        var (handler, httpContext) = await CreateHandlerAsync(authorization: null, _hash);
        await handler.ChallengeAsync(properties: null);
        Assert.AreEqual(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        var header = httpContext.Response.Headers.WWWAuthenticate.ToString();
        StringAssert.StartsWith(header, "Basic ");
        StringAssert.Contains(header, "realm=");
    }
}
