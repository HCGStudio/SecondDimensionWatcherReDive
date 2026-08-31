using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

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
        var time = new RefreshTokenStoreTests.ManualTimeProvider();
        var controller = CreateController(time);
        var loginResponse = await controller.Login(new LoginData(Password), CancellationToken.None);
        var first = (LoginResult)((OkObjectResult)loginResponse).Value!;

        var refreshResponse = await controller.Refresh(
            new AuthRequest(first.Token!, first.RefreshToken!),
            CancellationToken.None);
        var second = (LoginResult)((OkObjectResult)refreshResponse).Value!;
        Assert.AreNotEqual(first.RefreshToken, second.RefreshToken);

        var concurrentDuplicate = await controller.Refresh(
            new AuthRequest(first.Token!, first.RefreshToken!),
            CancellationToken.None);
        var duplicate = (LoginResult)((OkObjectResult)concurrentDuplicate).Value!;
        Assert.AreEqual(second.RefreshToken, duplicate.RefreshToken);
        Assert.AreEqual(
            new JwtSecurityTokenHandler().ReadJwtToken(second.Token).Id,
            new JwtSecurityTokenHandler().ReadJwtToken(duplicate.Token).Id);

        time.Advance(TimeSpan.FromSeconds(4));
        var replay = await controller.Refresh(
            new AuthRequest(first.Token!, first.RefreshToken!),
            CancellationToken.None);
        Assert.IsInstanceOfType<BadRequestObjectResult>(replay);

        var descendant = await controller.Refresh(
            new AuthRequest(second.Token!, second.RefreshToken!),
            CancellationToken.None);
        Assert.IsInstanceOfType<BadRequestObjectResult>(descendant);
    }

    [TestMethod]
    public async Task ConcurrentFirstRegistration_HasOneDurableWinner_AndWritesPrivateAtomicFile()
    {
        var repository = new InMemoryAuthenticationStateRepository();
        var directory = Path.Combine(Path.GetTempPath(), $"sdw-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var passwordFile = Path.Combine(directory, "password.json");
        var passwords = Enumerable.Range(0, 8).Select(index => $"candidate-{index}").ToArray();
        try
        {
            var controllers = passwords
                .Select(_ => CreateController(
                    authenticationStateRepository: repository,
                    configuredPassword: null,
                    passwordFile: passwordFile))
                .ToArray();

            var registrations = await Task.WhenAll(controllers.Select((controller, index) =>
                controller.Register(new LoginData(passwords[index]), CancellationToken.None)));

            var winnerIndex = Array.FindIndex(registrations, result => result is OkObjectResult);
            Assert.IsGreaterThanOrEqualTo(0, winnerIndex);
            Assert.AreEqual(1, registrations.Count(result => result is OkObjectResult));
            Assert.AreEqual(7, registrations.Count(result => result is BadRequestResult));
            var winnerRegistration = (LoginResult)((OkObjectResult)registrations[winnerIndex]).Value!;
            var winnerRefresh = await controllers[winnerIndex].Refresh(
                new AuthRequest(winnerRegistration.Token!, winnerRegistration.RefreshToken!),
                CancellationToken.None);
            Assert.IsInstanceOfType<OkObjectResult>(winnerRefresh);

            for (var index = 0; index < passwords.Length; index++)
            {
                var login = await controllers[0].Login(
                    new LoginData(passwords[index]), CancellationToken.None);
                Assert.AreEqual(index == winnerIndex, login is OkObjectResult);
            }

            var passwordConfig = JsonSerializer.Deserialize(
                await File.ReadAllTextAsync(passwordFile),
                AppJsonSerializerContext.Default.PasswordConfig);
            Assert.IsNotNull(passwordConfig);
            Assert.IsTrue(BCrypt.Net.BCrypt.Verify(
                passwords[winnerIndex],
                passwordConfig.Password.Value));
            if (!OperatingSystem.IsWindows())
            {
                Assert.AreEqual(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(passwordFile));
            }
            Assert.IsEmpty(Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CompatibilityFileFailure_DoesNotUndoDurablePasswordClaim()
    {
        var repository = new InMemoryAuthenticationStateRepository();
        var directory = Path.Combine(Path.GetTempPath(), $"sdw-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var controller = CreateController(
                authenticationStateRepository: repository,
                configuredPassword: null,
                passwordFile: directory);

            var registration = await controller.Register(
                new LoginData(Password), CancellationToken.None);
            var login = await controller.Login(new LoginData(Password), CancellationToken.None);
            var secondController = CreateController(
                authenticationStateRepository: repository,
                configuredPassword: null,
                passwordFile: Path.Combine(directory, "another-password.json"));
            var secondRegistration = await secondController.Register(
                new LoginData("different-administrator"), CancellationToken.None);
            var losingPasswordLogin = await secondController.Login(
                new LoginData("different-administrator"), CancellationToken.None);

            Assert.IsInstanceOfType<OkObjectResult>(registration);
            Assert.IsInstanceOfType<OkObjectResult>(login);
            Assert.IsInstanceOfType<BadRequestResult>(secondRegistration);
            Assert.IsInstanceOfType<BadRequestResult>(losingPasswordLogin);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacyConfiguredPassword_IsImportedOnce_AndDatabaseBecomesAuthoritative()
    {
        var repository = new InMemoryAuthenticationStateRepository();
        var legacyHash = BCrypt.Net.BCrypt.HashPassword(Password);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Password:Value"] = legacyHash
            })
            .Build();
        var initializer = new AuthenticationStateInitializer(
            configuration,
            repository,
            TimeProvider.System,
            NullLogger<AuthenticationStateInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        var controllerWithStaleFile = CreateController(
            authenticationStateRepository: repository,
            configuredPassword: "stale-or-replaced-password");
        var legacyLogin = await controllerWithStaleFile.Login(
            new LoginData(Password), CancellationToken.None);
        var staleFileLogin = await controllerWithStaleFile.Login(
            new LoginData("stale-or-replaced-password"), CancellationToken.None);
        var registration = await controllerWithStaleFile.Register(
            new LoginData("second-administrator"), CancellationToken.None);
        var allowRegister = await controllerWithStaleFile.CanRegister(CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(legacyLogin);
        Assert.IsInstanceOfType<BadRequestResult>(staleFileLogin);
        Assert.IsInstanceOfType<BadRequestResult>(registration);
        var allowPayload = ((OkObjectResult)allowRegister).Value;
        Assert.IsNotNull(allowPayload);
        var allow = (bool)allowPayload.GetType().GetProperty("Allow")!.GetValue(allowPayload)!;
        Assert.IsFalse(allow);
    }

    private static AuthController CreateController(
        TimeProvider? timeProvider = null,
        IAuthenticationStateRepository? authenticationStateRepository = null,
        string? configuredPassword = Password,
        string? passwordFile = null)
    {
        var configurationValues = new Dictionary<string, string?>
        {
            ["JwtSecret"] = Secret,
            ["Password:Value"] = configuredPassword is null
                ? null
                : BCrypt.Net.BCrypt.HashPassword(configuredPassword),
            ["PasswordFile"] = passwordFile
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        var security = new TokenSecurityOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenMinutes = 10,
            RefreshTokenDays = 30,
            RefreshTokenReuseGraceSeconds = 3
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
        timeProvider ??= TimeProvider.System;
        var store = new RefreshTokenStore(
            new MemoryRefreshTokenStorage(),
            Options.Create(security),
            timeProvider);
        return new AuthController(
            configuration,
            authenticationStateRepository ?? new InMemoryAuthenticationStateRepository(),
            validation,
            store,
            Options.Create(security),
            timeProvider,
            NullLogger<AuthController>.Instance);
    }

    private sealed class InMemoryAuthenticationStateRepository : IAuthenticationStateRepository
    {
        private string? _passwordHash;

        public Task<string?> GetPasswordHashAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Volatile.Read(ref _passwordHash));

        public Task<bool> TryClaimPasswordAsync(
            string passwordHash,
            Guid claimId,
            DateTimeOffset registeredAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(Interlocked.CompareExchange(ref _passwordHash, passwordHash, null) is null);
    }
}
