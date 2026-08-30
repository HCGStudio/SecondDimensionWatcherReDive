using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Moq;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.IntegrationTest.Helpers;
using SecondDimensionWatcherReDive.MigrationTasks;
using FileMapping = SecondDimensionWatcherReDive.Framework.DataRepository.FileMapping;
using ApplicationContext = SecondDimensionWatcherReDive.Models.ApplicationContext;

namespace SecondDimensionWatcherReDive.IntegrationTest;

internal sealed class WebDavWebApplicationFactory : WebApplicationFactory<MigrationTaskRunner>
{
    public const string TestUserName = "sdwuser";
    public const string TestPassword = "test-pass";
    public const string JwtSecret = "integration-test-jwt-secret-must-be-long-enough-32-bytes";
    public static readonly Guid UserId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ProfileId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid SessionId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset AuthenticatedAt = DateTimeOffset.FromUnixTimeSeconds(
        DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    static WebDavWebApplicationFactory()
    {
        // Program.cs reads configuration synchronously inside the entry point, BEFORE
        // builder.Build() runs. WebApplicationFactory's ConfigureAppConfiguration
        // callback only fires at Build time, so it cannot satisfy those reads.
        // WebApplication.CreateBuilder calls AddEnvironmentVariables() unprefixed,
        // so process env vars set here are visible to builder.Configuration.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("JwtSecret", JwtSecret);
        Environment.SetEnvironmentVariable("ConnectionStrings__sdw",
            "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("Torrent__Remote__Url", "http://localhost/");
        Environment.SetEnvironmentVariable("FileStore__Local", "/tmp/sdw-test");
        Environment.SetEnvironmentVariable("AI__OpenAI__ApiKey", string.Empty);
        Environment.SetEnvironmentVariable("AI__Anthropic__ApiKey", string.Empty);
        Environment.SetEnvironmentVariable("TmdbApiKey", string.Empty);
        Environment.SetEnvironmentVariable("Valkey__ConnectionString", string.Empty);
    }

    public List<FileMapping> Mappings { get; } = new();
    public Mock<IFileStore> FileStoreMock { get; } = new();
    public Mock<IFileStoreProvider> FileStoreProviderMock { get; } = new();
    public Helpers.FakeFileMappingRepository MappingRepository { get; }
    public FakeWebDavTokenRepository DeviceTokenRepository { get; }

    private readonly object _mappingsLock = new();
    private readonly UserRole _role;
    private bool _sessionRevoked;

    public WebDavWebApplicationFactory(
        string virtualRoot = "/",
        UserRole role = UserRole.Admin)
    {
        _role = role;
        MappingRepository = new Helpers.FakeFileMappingRepository(Mappings);
        DeviceTokenRepository = new FakeWebDavTokenRepository(
            UserId,
            TestUserName,
            BCrypt.Net.BCrypt.HashPassword(TestPassword),
            virtualRoot);
        FileStoreMock.SetupGet(s => s.Name).Returns("local");
        FileStoreProviderMock
            .Setup(p => p.GetRequiredClient(It.IsAny<string>()))
            .Returns(FileStoreMock.Object);
        FileStoreProviderMock
            .Setup(p => p.GetClient(It.IsAny<string>()))
            .Returns(FileStoreMock.Object);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Defensive: also push the same values via in-memory config in case the
            // host reloads configuration after env vars have been read.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSecret"] = JwtSecret,
                ["ConnectionStrings:sdw"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Torrent:Remote:Url"] = "http://localhost/",
                ["FileStore:Local"] = "/tmp/sdw-test",
                ["AI:OpenAI:ApiKey"] = string.Empty,
                ["AI:Anthropic:ApiKey"] = string.Empty,
                ["TmdbApiKey"] = string.Empty,
                ["Valkey:ConnectionString"] = string.Empty,
                ["DisableCors"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace DbContext with one whose IMigrator is a no-op so MigrateAsync never connects.
            services.RemoveAll<DbContextOptions<ApplicationContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<ApplicationContext>(options =>
            {
                options.UseNpgsql("Host=localhost;Database=test;Username=test;Password=test");
                options.ReplaceService<IMigrator, NoOpMigrator>();
            });

            // Strip all registered IMigrationTask so MigrationTaskRunner.RunAsync iterates an empty collection.
            services.RemoveAll<IMigrationTask>();
            services.RemoveAll<IMigrationLock>();
            services.AddSingleton<IMigrationLock, NoOpMigrationLock>();

            // Strip all hosted services originating from this solution so background loops never start.
            var hostedToRemove = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .Where(d =>
                {
                    var asmName = d.ImplementationType?.Assembly.GetName().Name
                                  ?? d.ImplementationFactory?.Method.DeclaringType?.Assembly.GetName().Name;
                    return asmName != null && asmName.StartsWith("SecondDimensionWatcherReDive", StringComparison.Ordinal);
                })
                .ToList();
            foreach (var d in hostedToRemove) services.Remove(d);

            // Replace storage / mapping / explorer with our test doubles.
            services.RemoveAll<IFileStore>();
            services.RemoveAll<IFileStoreProvider>();
            services.RemoveAll<IFileMappingRepository>();
            services.RemoveAll<IFileExplorer>();
            services.RemoveAll<IWebDavTokenRepository>();
            services.RemoveAll<IIdentityRepository>();
            services.RemoveAll<IApplicationSettingsRepository>();

            services.AddSingleton(FileStoreMock.Object);
            services.AddSingleton(FileStoreProviderMock.Object);
            services.AddSingleton<IFileMappingRepository>(_ => MappingRepository);
            services.AddSingleton<IFileExplorer>(_ => new FakeFileExplorer(Mappings, FileStoreMock.Object, MappingRepository));
            services.AddSingleton<IWebDavTokenRepository>(_ => DeviceTokenRepository);
            var identityRepository = new Mock<IIdentityRepository>();
            var user = new UserAccount(
                UserId,
                TestUserName,
                "hash",
                _role,
                false,
                AuthenticatedAt,
                AuthenticatedAt);
            var profile = new UserProfile(
                ProfileId,
                UserId,
                "Test",
                null,
                null,
                true,
                AuthenticatedAt,
                AuthenticatedAt);
            var session = new UserSession(
                SessionId,
                UserId,
                ProfileId,
                "hash",
                "integration-test",
                AuthenticatedAt,
                AuthenticatedAt,
                AuthenticatedAt,
                DateTimeOffset.UtcNow.AddDays(1),
                null);
            identityRepository.Setup(repository => repository.FindUserByIdAsync(
                    UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);
            identityRepository.Setup(repository => repository.GetAuthenticatedSessionAsync(
                    SessionId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _sessionRevoked
                    ? null
                    : new AuthenticatedSession(user, profile, session));
            services.AddSingleton(identityRepository.Object);
            services.AddSingleton<IApplicationSettingsRepository, FakeApplicationSettingsRepository>();
        });
    }

    public void ResetState()
    {
        lock (_mappingsLock) Mappings.Clear();
        MappingRepository.PrefixCalls.Clear();
        FileStoreMock.Reset();
        FileStoreMock.SetupGet(s => s.Name).Returns("local");
        FileStoreProviderMock.Reset();
        FileStoreProviderMock
            .Setup(p => p.GetRequiredClient(It.IsAny<string>()))
            .Returns(FileStoreMock.Object);
        FileStoreProviderMock
            .Setup(p => p.GetClient(It.IsAny<string>()))
            .Returns(FileStoreMock.Object);
    }

    public HttpClient CreateBasicAuthClient(string user = TestUserName, string pass = TestPassword)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var bytes = Encoding.UTF8.GetBytes($"{user}:{pass}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        return client;
    }

    public HttpClient CreateUnauthenticatedClient()
        => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void RevokeLoginSession() => _sessionRevoked = true;

    public HttpClient CreateJwtClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var keyBytes = Encoding.ASCII.GetBytes(JwtSecret);
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(ClaimTypes.Name, TestUserName),
                new Claim(ClaimTypes.Role, _role.ToString()),
                new Claim(IdentityClaimTypes.UserId, UserId.ToString()),
                new Claim(IdentityClaimTypes.ProfileId, ProfileId.ToString()),
                new Claim(IdentityClaimTypes.SessionId, SessionId.ToString()),
                new Claim(IdentityClaimTypes.AuthenticatedAt,
                    AuthenticatedAt.ToUnixTimeSeconds().ToString())
            ],
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return client;
    }

    private sealed class NoOpMigrator : IMigrator
    {
        public void Migrate(string? targetMigration = null) { }

        public Task MigrateAsync(string? targetMigration = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public string GenerateScript(
            string? fromMigration = null,
            string? toMigration = null,
            MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
            => string.Empty;

        public bool HasPendingModelChanges() => false;
    }

    private sealed class NoOpMigrationLock : IMigrationLock, IMigrationLockLease
    {
        public Task<IMigrationLockLease> AcquireAsync(CancellationToken cancellationToken)
            => Task.FromResult<IMigrationLockLease>(this);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeApplicationSettingsRepository : IApplicationSettingsRepository
    {
        private readonly object _gate = new();
        private ApplicationSettings? _settings;

        public Task<ApplicationSettings?> GetAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
                return Task.FromResult(_settings);
        }

        public Task<ApplicationSettings?> TrySaveAsync(
            string valuesJson,
            string? protectedSecrets,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var currentRevision = _settings?.Revision ?? 0;
                if (currentRevision != expectedRevision)
                    return Task.FromResult<ApplicationSettings?>(null);

                _settings = new ApplicationSettings(
                    1,
                    valuesJson,
                    protectedSecrets,
                    checked(currentRevision + 1),
                    updatedAt);
                return Task.FromResult<ApplicationSettings?>(_settings);
            }
        }
    }
}
