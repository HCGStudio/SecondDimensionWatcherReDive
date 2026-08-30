using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class AccountsControllerTests
{
    private static readonly Guid UserId = Guid.Parse("61000000-0000-0000-0000-000000000001");
    private static readonly Guid ActiveProfileId = Guid.Parse("62000000-0000-0000-0000-000000000001");
    private static readonly Guid ProtectedProfileId = Guid.Parse("62000000-0000-0000-0000-000000000002");

    [TestMethod]
    public async Task ProfileA_CannotClearProfileBPin_WithoutPinOrRecentAuthentication()
    {
        var repository = new Mock<IIdentityRepository>();
        repository.Setup(candidate => candidate.FindProfileAsync(
                ProtectedProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile(ProtectedProfileId, BCrypt.Net.BCrypt.HashPassword("2468")));
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AccessPolicies.RecentAuthentication))
            .ReturnsAsync(AuthorizationResult.Failed());
        var controller = CreateController(repository, authorization);

        var result = await controller.UpdateProfile(
            ProtectedProfileId,
            new UpdateProfileRequest(
                "Protected",
                null,
                Pin: string.Empty,
                CurrentPin: null,
                ReplacePin: true),
            CancellationToken.None);

        Assert.IsInstanceOfType<ForbidResult>(result);
        repository.Verify(candidate => candidate.UpdateProfileAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ProfileBPin_AllowsIntentionalPinReplacement()
    {
        var repository = new Mock<IIdentityRepository>();
        repository.Setup(candidate => candidate.FindProfileAsync(
                ProtectedProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile(ProtectedProfileId, BCrypt.Net.BCrypt.HashPassword("2468")));
        repository.Setup(candidate => candidate.UpdateProfileAsync(
                ProtectedProfileId,
                UserId,
                "Protected",
                null,
                It.IsAny<string?>(),
                true,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                AccessPolicies.RecentAuthentication))
            .ReturnsAsync(AuthorizationResult.Failed());
        var controller = CreateController(repository, authorization);

        var result = await controller.UpdateProfile(
            ProtectedProfileId,
            new UpdateProfileRequest(
                "Protected",
                null,
                Pin: "1357",
                CurrentPin: "2468",
                ReplacePin: true),
            CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        repository.Verify(candidate => candidate.UpdateProfileAsync(
                ProtectedProfileId,
                UserId,
                "Protected",
                null,
                It.Is<string?>(hash => hash != null
                    && BCrypt.Net.BCrypt.Verify("1357", hash)),
                true,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task CreateProfile_DuplicateName_ReturnsConflict()
    {
        var repository = new Mock<IIdentityRepository>();
        repository.Setup(candidate => candidate.AddProfileAsync(
                It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityConflictException("duplicate"));
        var controller = CreateController(
            repository, new Mock<IAuthorizationService>());

        var result = await controller.CreateProfile(
            new CreateProfileRequest("Protected", null, null),
            CancellationToken.None);

        Assert.IsInstanceOfType<ConflictResult>(result);
    }

    [TestMethod]
    public async Task UpdateProfile_DuplicateSiblingName_ReturnsConflict()
    {
        var repository = new Mock<IIdentityRepository>();
        repository.Setup(candidate => candidate.FindProfileAsync(
                ActiveProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile(ActiveProfileId, null));
        repository.Setup(candidate => candidate.UpdateProfileAsync(
                ActiveProfileId,
                UserId,
                "Protected",
                null,
                null,
                false,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdentityConflictException("duplicate"));
        var controller = CreateController(
            repository, new Mock<IAuthorizationService>());

        var result = await controller.UpdateProfile(
            ActiveProfileId,
            new UpdateProfileRequest("Protected", null, null),
            CancellationToken.None);

        Assert.IsInstanceOfType<ConflictResult>(result);
    }

    private static AccountsController CreateController(
        Mock<IIdentityRepository> repository,
        Mock<IAuthorizationService> authorization)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSecret"] = "unit-test-secret-long-enough-for-hmac-1234"
            })
            .Build();
        return new AccountsController(
            repository.Object,
            new SessionTokenIssuer(configuration, repository.Object),
            authorization.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(IdentityClaimTypes.UserId, UserId.ToString()),
                        new Claim(IdentityClaimTypes.ProfileId, ActiveProfileId.ToString()),
                        new Claim(ClaimTypes.Role, nameof(UserRole.Member))
                    ], "test"))
                }
            }
        };
    }

    private static UserProfile Profile(Guid id, string? pinHash) => new(
        id,
        UserId,
        "Protected",
        null,
        pinHash,
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);
}
