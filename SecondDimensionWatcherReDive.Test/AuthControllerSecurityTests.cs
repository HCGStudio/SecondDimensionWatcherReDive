using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class AuthControllerSecurityTests
{
    private const string Password = "correct-horse-battery-staple";
    private const string Secret = "test-jwt-secret-with-more-than-32-bytes-of-entropy";

    [TestMethod]
    public async Task LoginIssuesExpiringIssuerAndAudienceBoundJwt()
    {
        var controller = CreateController();

        var response = await controller.Login(new LoginData(Password), CancellationToken.None);

        var login = (LoginResult)((OkObjectResult)response).Value!;
        Assert.IsTrue(login.Success);
        Assert.IsFalse(string.IsNullOrEmpty(login.RefreshToken));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.Token);
        Assert.AreEqual("test-issuer", jwt.Issuer);
        CollectionAssert.Contains(jwt.Audiences.ToList(), "test-audience");
        Assert.IsTrue(jwt.ValidTo > DateTime.UtcNow);
        Assert.IsFalse(string.IsNullOrEmpty(jwt.Id));
    }

    [TestMethod]
    public async Task RefreshRotatesOnceAndReplayRevokesDescendants()
    {
        var controller = CreateController();
        var loginResponse = await controller.Login(new LoginData(Password), CancellationToken.None);
        var first = (LoginResult)((OkObjectResult)loginResponse).Value!;

        var refreshResponse = await controller.Refresh(
            new AuthRequest(first.Token!, first.RefreshToken!),
            CancellationToken.None);
        var second = (LoginResult)((OkObjectResult)refreshResponse).Value!;
        Assert.AreNotEqual(first.RefreshToken, second.RefreshToken);

        var replay = await controller.Refresh(
            new AuthRequest(first.Token!, first.RefreshToken!),
            CancellationToken.None);
        Assert.IsInstanceOfType<BadRequestObjectResult>(replay);

        var descendant = await controller.Refresh(
            new AuthRequest(second.Token!, second.RefreshToken!),
            CancellationToken.None);
        Assert.IsInstanceOfType<BadRequestObjectResult>(descendant);
    }

    private static AuthController CreateController()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSecret"] = Secret,
                ["Password:Value"] = BCrypt.Net.BCrypt.HashPassword(Password)
            })
            .Build();
        var security = new TokenSecurityOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenMinutes = 10,
            RefreshTokenDays = 30
        };
        var validation = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = security.Issuer,
            ValidateAudience = true,
            ValidAudience = security.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var provider = services.BuildServiceProvider();
        var store = new RefreshTokenStore(
            provider.GetRequiredService<IDistributedCache>(),
            Options.Create(security),
            TimeProvider.System);
        return new AuthController(
            configuration,
            validation,
            store,
            Options.Create(security),
            TimeProvider.System,
            NullLogger<AuthController>.Instance);
    }
}
