using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class WebDavTokenControllerTests
{
    private Mock<IWebDavTokenRepository> _repo = null!;
    private WebDavTokenController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _repo = new Mock<IWebDavTokenRepository>();
        _repo.Setup(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _controller = new WebDavTokenController(_repo.Object);
    }

    [TestMethod]
    public async Task ListTokens_ReturnsSummariesWithoutHash()
    {
        var seeded = new List<WebDavToken>
        {
            new(Guid.NewGuid(), "alice", "hash-1", "first key", DateTimeOffset.UtcNow.AddMinutes(-1)),
            new(Guid.NewGuid(), "bob", "hash-2", null, DateTimeOffset.UtcNow)
        };
        _repo.Setup(r => r.GetAllOrderedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(seeded);

        var result = await _controller.ListTokens(CancellationToken.None) as OkObjectResult;
        Assert.IsNotNull(result);
        var payload = (List<WebDavTokenSummary>)result.Value!;
        Assert.AreEqual(2, payload.Count);
        Assert.IsTrue(payload.All(p => !p.GetType().GetProperties().Any(prop => prop.Name == "TokenHash")));
        Assert.AreEqual("alice", payload[0].Username);
        Assert.AreEqual("first key", payload[0].Description);
    }

    [TestMethod]
    public async Task CreateToken_AutoGeneratesUsernameWhenMissing()
    {
        WebDavToken? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<WebDavToken>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<WebDavToken, CancellationToken>((t, _) => captured = t);

        var response = await _controller.CreateToken(
            new CreateWebDavTokenRequest(null, null),
            CancellationToken.None) as OkObjectResult;

        Assert.IsNotNull(response);
        var payload = (CreateWebDavTokenResponse)response.Value!;
        Assert.IsTrue(payload.Username.StartsWith("sdw-"));
        Assert.AreEqual(12, payload.Username.Length); // "sdw-" + 8 chars
        Assert.IsFalse(string.IsNullOrWhiteSpace(payload.Token));
        Assert.IsNotNull(captured);
        Assert.AreEqual(payload.Username, captured!.Username);
        Assert.AreNotEqual(payload.Token, captured.TokenHash, "TokenHash must not be plaintext.");
        Assert.IsTrue(BCrypt.Net.BCrypt.Verify(payload.Token, captured.TokenHash));
        Assert.IsNull(captured.Description);
    }

    [TestMethod]
    public async Task CreateToken_StoresProvidedUsernameAndDescription()
    {
        WebDavToken? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<WebDavToken>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<WebDavToken, CancellationToken>((t, _) => captured = t);

        var response = await _controller.CreateToken(
            new CreateWebDavTokenRequest("Carol_2", "  desktop  "),
            CancellationToken.None) as OkObjectResult;

        Assert.IsNotNull(response);
        var payload = (CreateWebDavTokenResponse)response.Value!;
        Assert.AreEqual("Carol_2", payload.Username);
        Assert.AreEqual("desktop", captured!.Description);
        Assert.AreEqual("desktop", payload.Description);
    }

    [TestMethod]
    public async Task CreateToken_RejectsInvalidUsername()
    {
        var response = await _controller.CreateToken(
            new CreateWebDavTokenRequest("ab", null),
            CancellationToken.None);
        Assert.IsInstanceOfType(response, typeof(BadRequestObjectResult));

        var spaceResponse = await _controller.CreateToken(
            new CreateWebDavTokenRequest("with space", null),
            CancellationToken.None);
        Assert.IsInstanceOfType(spaceResponse, typeof(BadRequestObjectResult));

        _repo.Verify(r => r.AddAsync(It.IsAny<WebDavToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateToken_ReturnsConflictWhenUsernameTaken()
    {
        _repo.Setup(r => r.ExistsByUsernameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var response = await _controller.CreateToken(
            new CreateWebDavTokenRequest("alice", null),
            CancellationToken.None);
        Assert.IsInstanceOfType(response, typeof(ConflictObjectResult));
        _repo.Verify(r => r.AddAsync(It.IsAny<WebDavToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteToken_ReturnsNotFoundWhenMissing()
    {
        _repo.Setup(r => r.RemoveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var response = await _controller.DeleteToken(Guid.NewGuid(), CancellationToken.None);
        Assert.IsInstanceOfType(response, typeof(NotFoundResult));
    }

    [TestMethod]
    public async Task DeleteToken_ReturnsNoContentOnSuccess()
    {
        _repo.Setup(r => r.RemoveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var response = await _controller.DeleteToken(Guid.NewGuid(), CancellationToken.None);
        Assert.IsInstanceOfType(response, typeof(NoContentResult));
    }
}
