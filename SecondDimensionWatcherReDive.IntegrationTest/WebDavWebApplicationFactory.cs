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
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
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
    public Mock<IAnimationInfoRepository> AnimationInfoRepositoryMock { get; } = new();
    public RecordingLoggerProvider Logs { get; } = new();
    public Helpers.FakeFileMappingRepository MappingRepository { get; }

    private readonly object _mappingsLock = new();

    public WebDavWebApplicationFactory()
    {
        MappingRepository = new Helpers.FakeFileMappingRepository(Mappings);
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
        builder.ConfigureLogging(logging => logging.AddFilter<RecordingLoggerProvider>(
            "Microsoft.AspNetCore.Hosting.Diagnostics",
            LogLevel.Trace));

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
            services.RemoveAll<IApplicationSettingsRepository>();
            services.RemoveAll<IAuthenticationStateRepository>();
            services.RemoveAll<IAnimationInfoRepository>();

            services.AddSingleton(FileStoreMock.Object);
            services.AddSingleton(FileStoreProviderMock.Object);
            services.AddSingleton<IFileMappingRepository>(_ => MappingRepository);
            services.AddSingleton<IFileExplorer>(_ => new FakeFileExplorer(Mappings, FileStoreMock.Object, MappingRepository));
            services.AddSingleton<IWebDavTokenRepository>(_ =>
                new FakeWebDavTokenRepository(TestUserName, BCrypt.Net.BCrypt.HashPassword(TestPassword)));
            services.AddSingleton<IApplicationSettingsRepository, FakeApplicationSettingsRepository>();
            services.AddSingleton<IAuthenticationStateRepository, FakeAuthenticationStateRepository>();
            services.AddSingleton(AnimationInfoRepositoryMock.Object);
            services.AddSingleton<ILoggerProvider>(Logs);
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
        AnimationInfoRepositoryMock.Reset();
        Logs.Clear();
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

    public HttpClient CreateJwtClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var keyBytes = Encoding.ASCII.GetBytes(JwtSecret);
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "SecondDimensionWatcherReDive",
            audience: "SecondDimensionWatcherReDive.Client",
            claims:
            [
                new Claim(ClaimTypes.Name, TestUserName),
                new Claim("Id", Guid.Empty.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
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

    private sealed class FakeAuthenticationStateRepository : IAuthenticationStateRepository
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

    internal sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(_messages);

        public void Dispose() { }

        public void Clear()
        {
            while (_messages.TryDequeue(out _)) { }
        }

        private sealed class RecordingLogger(
            System.Collections.Concurrent.ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
