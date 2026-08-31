using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.Auth;

namespace SecondDimensionWatcherReDive.IntegrationTest.Auth;

[TestClass]
public sealed class LogoutRateLimitTests
{
    [TestMethod]
    public async Task ExhaustedAuthenticationBucket_DoesNotBlockRefreshRevocation()
    {
        await using var factory = new WebDavWebApplicationFactory();
        var refreshTokens = factory.Services.GetRequiredService<RefreshTokenStore>();
        var first = await refreshTokens.IssueAsync("jwt-1", CancellationToken.None);
        Assert.IsNotNull(first);
        var descendant = await refreshTokens.RotateAsync(
            first.Token,
            "jwt-1",
            "jwt-2",
            CancellationToken.None);
        Assert.IsNotNull(descendant);
        using var client = factory.CreateUnauthenticatedClient();

        HttpResponseMessage response = null!;
        for (var index = 0; index < 11; index++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { password = $"invalid-{index}" });
        }
        using (response)
            Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);

        using var logout = await client.PostAsJsonAsync(
            "/api/auth/logout",
            new { refreshToken = first.Token });

        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.IsNull(await refreshTokens.RotateAsync(
            descendant.Token,
            "jwt-2",
            "jwt-3",
            CancellationToken.None));
    }
}
