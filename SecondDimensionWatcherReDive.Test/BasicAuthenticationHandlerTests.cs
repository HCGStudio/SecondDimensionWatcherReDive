using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class BasicAuthenticationHandlerTests
{
    private const string ValidUser = "alice";
    private const string ValidPassword = "correct-horse";
    private static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private string _hash = null!;

    [TestInitialize]
    public void Setup()
    {
        _hash = BCrypt.Net.BCrypt.HashPassword(ValidPassword);
    }

    private async Task<(BasicAuthenticationHandler handler, DefaultHttpContext httpContext, Mock<IWebDavTokenRepository> repo)> CreateHandlerAsync(
        string? authorization,
        WebDavToken? seededToken)
    {
        var repo = new Mock<IWebDavTokenRepository>();
        repo.Setup(r => r.FindByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string username, CancellationToken _) =>
                seededToken is not null && seededToken.Username == username ? seededToken : null);

        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        var identityRepo = new Mock<IIdentityRepository>();
        identityRepo.Setup(r => r.FindUserByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount(
                UserId, "admin", "hash", UserRole.Admin, false,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        services.AddSingleton(identityRepo.Object);
        var provider = services.BuildServiceProvider();

        var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Setup(m => m.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var handler = new BasicAuthenticationHandler(
            optionsMonitor.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        if (authorization is not null) httpContext.Request.Headers["Authorization"] = authorization;

        var scheme = new AuthenticationScheme(BasicAuthenticationHandler.SchemeName, null, typeof(BasicAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);
        return (handler, httpContext, repo);
    }

    private WebDavToken SeededToken(
        string username = ValidUser,
        string scope = "read",
        string root = "/Anime",
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null) =>
        new(Guid.NewGuid(), UserId, username, _hash, null, DateTimeOffset.UtcNow,
            scope, root, expiresAt ?? DateTimeOffset.UtcNow.AddDays(1), revokedAt);

    private static string BasicHeader(string user, string password) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));

    [TestMethod]
    public async Task NoAuthorizationHeader_ReturnsNoResult()
    {
        var (handler, _, _) = await CreateHandlerAsync(authorization: null, SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsTrue(result.None);
    }

    [TestMethod]
    public async Task WrongScheme_ReturnsNoResult()
    {
        var (handler, _, _) = await CreateHandlerAsync("Bearer abc", SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsTrue(result.None);
    }

    [TestMethod]
    public async Task MalformedBase64_Fails()
    {
        var (handler, _, _) = await CreateHandlerAsync("Basic !!!not-base64!!!", SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.Failure);
    }

    [TestMethod]
    public async Task MissingColonSeparator_Fails()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("noseparator"));
        var (handler, _, _) = await CreateHandlerAsync($"Basic {encoded}", SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task UnknownUser_Fails()
    {
        var (handler, _, _) = await CreateHandlerAsync(BasicHeader("nobody", ValidPassword), SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task WrongPassword_Fails()
    {
        var (handler, _, _) = await CreateHandlerAsync(BasicHeader(ValidUser, "wrong"), SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task ValidCredentials_Succeed()
    {
        var (handler, _, _) = await CreateHandlerAsync(BasicHeader(ValidUser, ValidPassword), SeededToken());
        var result = await handler.AuthenticateAsync();
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(ValidUser, result.Principal!.Identity!.Name);
        Assert.AreEqual("/Anime", result.Principal.FindFirst(IdentityClaimTypes.VirtualRoot)?.Value);
        Assert.AreEqual("read", result.Principal.FindFirst(IdentityClaimTypes.DeviceScope)?.Value);
        Assert.AreEqual(UserId.ToString(), result.Principal.FindFirst(IdentityClaimTypes.UserId)?.Value);
    }

    [TestMethod]
    public async Task RevokedExpiredOrNonReadToken_Fails()
    {
        var revoked = await CreateHandlerAsync(
            BasicHeader(ValidUser, ValidPassword),
            SeededToken(revokedAt: DateTimeOffset.UtcNow));
        Assert.IsFalse((await revoked.handler.AuthenticateAsync()).Succeeded);

        var expired = await CreateHandlerAsync(
            BasicHeader(ValidUser, ValidPassword),
            SeededToken(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.IsFalse((await expired.handler.AuthenticateAsync()).Succeeded);

        var write = await CreateHandlerAsync(
            BasicHeader(ValidUser, ValidPassword),
            SeededToken(scope: "write"));
        Assert.IsFalse((await write.handler.AuthenticateAsync()).Succeeded);
    }

    [TestMethod]
    public async Task Challenge_SetsBasicWwwAuthenticate()
    {
        var (handler, httpContext, _) = await CreateHandlerAsync(authorization: null, SeededToken());
        await handler.ChallengeAsync(properties: null);
        Assert.AreEqual(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        var header = httpContext.Response.Headers.WWWAuthenticate.ToString();
        StringAssert.StartsWith(header, "Basic ");
        StringAssert.Contains(header, "realm=");
    }
}
