using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using AspSpaService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SecondDimensionWatcherReDive;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.WebDav;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Inference.AI;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.Repositories;
using SecondDimensionWatcherReDive.Chat;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.MigrationTasks;
using SecondDimensionWatcherReDive.Utils.Feed;
using SecondDimensionWatcherReDive.Utils.FileDownload;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.MetadataReview;
using SecondDimensionWatcherReDive.Utils.Incidents;
using SecondDimensionWatcherReDive.Utils.Scraper;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSystemd();

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.TypeInfoResolverChain.Add(SecondDimensionWatcherReDive.Controllers.External
            .AppJsonSerializerContext.Default);
        options.JsonSerializerOptions.TypeInfoResolverChain.Add(SecondDimensionWatcherReDive.Chat.External
            .ChatJsonSerializerContext.Default);
    })
    .AddApplicationPart(typeof(ChatServiceExtensions).Assembly)
    .ConfigureApplicationPartManager(manager =>
    {
        var defaultProvider = manager.FeatureProviders
            .OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerFeatureProvider>()
            .First();
        manager.FeatureProviders.Remove(defaultProvider);
        manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
    })
    .AddWebDav();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
if (builder.Configuration["Config"] is { } configPath)
    builder.Configuration.AddYamlFile(configPath, optional: false, reloadOnChange: true);
var passwordFile = builder.Configuration["PasswordFile"] ?? "password.json";
builder.Configuration.AddJsonFile(passwordFile, optional: true, reloadOnChange: true);
// Runtime settings are the highest-priority configuration source. The provider is populated
// from PostgreSQL after EF migrations and before hosted services start.
var runtimeSettingsProvider = builder.Configuration.AddRuntimeSettingsConfigurationProvider();

// Persist the key ring beside the password file by default so runtime secrets remain
// decryptable after restarts and container upgrades. Deployments may select another path.
var dataProtectionKeyRingPath = builder.Configuration["DataProtection:KeyRingPath"]
                                ?? Path.Combine(
                                    Path.GetDirectoryName(Path.GetFullPath(passwordFile))!,
                                    "data-protection-keys");
Directory.CreateDirectory(dataProtectionKeyRingPath);
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(
        dataProtectionKeyRingPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
builder.Services.AddDataProtection()
    .SetApplicationName("SecondDimensionWatcherReDive")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));
builder.Services.AddApplicationRuntimeSettings(runtimeSettingsProvider);

builder.Services.Configure<MediaLibraryOptions>(
    builder.Configuration.GetSection(MediaLibraryOptions.SectionName));
builder.Services.PostConfigure<MediaLibraryOptions>(options =>
{
    var localStore = builder.Configuration["FileStore:Local"] ?? "./download";
    options.DownloadRoot = Path.GetFullPath(localStore);
});
builder.Services.AddOptions<MigrationOptions>()
    .BindConfiguration(MigrationOptions.SectionName);

builder.Services.AddDbContext<ApplicationContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("sdw"),
        optionsBuilder => { optionsBuilder.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null); });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("all", policy =>
    {
        policy.AllowAnyHeader();
        policy.AllowAnyMethod();
        policy.AllowAnyOrigin();
    });
});

//Configure JWT
var key = Encoding.ASCII.GetBytes(builder.Configuration["JwtSecret"] ??
                                  throw new ApplicationException("JwtSecret must present in the config file."));

var tokenValidationParams = new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    IssuerSigningKey = new SymmetricSecurityKey(key),
    ValidateIssuer = false,
    ValidateAudience = false,
    ValidateLifetime = true,
    RequireExpirationTime = false
};

builder.Services.AddSingleton(tokenValidationParams);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.TokenValidationParameters = tokenValidationParams;
}).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BasicAuthenticationHandler>(
    BasicAuthenticationHandler.SchemeName, _ => { });

//Add distributed cache (Valkey / Redis or in-memory fallback)
var valkeyConnection = builder.Configuration["Valkey:ConnectionString"];
if (!string.IsNullOrEmpty(valkeyConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = valkeyConnection;
        options.InstanceName = builder.Configuration["Valkey:InstanceName"] ?? "sdw-redive:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

//Configure HTTP client
builder.Services.AddOptions<QBittorrentRemoteOptions>()
    .BindConfiguration(QBittorrentRemoteOptions.SectionName);
builder.Services.AddScoped<QBittorrentCookieStore>();
builder.Services.AddTransient<QBittorrentAuthHandler>();
builder.Services.AddHttpClient("RemoteTorrentDownloadClient", (serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<QBittorrentRemoteOptions>>()
        .CurrentValue;
    client.BaseAddress = new Uri(options.Url, UriKind.Absolute);
    var overrideUserAgent = options.UserAgent;
    if (overrideUserAgent != null)
    {
        client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, overrideUserAgent);
    }
    else
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SecondDimensionWatcher", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SecondDimensionWatcherReDive",
            Assembly.GetCallingAssembly().GetName().Version?.ToString() ?? "2.0"));
    }
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var cookieStore = serviceProvider.GetRequiredService<QBittorrentCookieStore>();
    return new HttpClientHandler
    {
        // Do not let a 307/308 replay qBittorrent credentials to another origin. Redirects are
        // intentionally surfaced to the caller so the configured endpoint can be corrected.
        AllowAutoRedirect = false,
        CookieContainer = cookieStore.Container,
        UseCookies = true
    };
})
.AddHttpMessageHandler<QBittorrentAuthHandler>();

builder.Services.AddHttpClient("Feed", client =>
{
    var overrideUserAgent = builder.Configuration["Feed:UserAgent"];
    if (overrideUserAgent != null)
    {
        client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, overrideUserAgent);
    }
    else
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SecondDimensionWatcher", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SecondDimensionWatcherReDive",
            Assembly.GetCallingAssembly().GetName().Version?.ToString() ?? "2.0"));
    }
});

var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings.Add(".mkv", "video/x-matroska");
builder.Services.AddSingleton<IContentTypeProvider>(contentTypeProvider);

//Add channels
builder.Services.AddSingleton(Channel.CreateUnbounded<RemoteTorrentTrackRequest>());
builder.Services.AddSingleton(Channel.CreateUnbounded<FileDownloadStatus>());
builder.Services.AddSingleton(Channel.CreateUnbounded<DownloadCompleteRequest>());

// Persistent incident inbox and health probes.
builder.Services.AddSingleton<IIncidentReporter, IncidentReporter>();
builder.Services.AddSingleton<IIncidentDiskProbe, IncidentDiskProbe>();

//Add hosting services
builder.Services.AddHostedService<CompleteDownloadBackgroundService>();
builder.Services.AddHostedService<FetchRemoteTorrentBackgroundService>();
builder.Services.AddHostedService<UpdateDownloadStatusBackgroundService>();
builder.Services.AddHostedService<IncidentReconciliationBackgroundService>();
builder.Services.AddSingleton<MediaLibraryScanQueue>();
builder.Services.AddSingleton<IMediaLibraryScanQueue>(sp =>
    sp.GetRequiredService<MediaLibraryScanQueue>());
builder.Services.AddHostedService<MediaLibraryScanBackgroundService>();

//Add scheduled tasks
builder.Services.AddSingleton<SyncFeed>();
builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<SyncFeed>());
builder.Services.AddHostedService<ScheduledTaskBackgroundService<SyncFeed>>();

builder.Services.AddSingleton<ScrapeSeasonBangumi>();
builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<ScrapeSeasonBangumi>());
builder.Services.AddHostedService<ScheduledTaskBackgroundService<ScrapeSeasonBangumi>>();

builder.Services.AddSingleton<ScanMediaLibraries>();
builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<ScanMediaLibraries>());
builder.Services.AddHostedService<ScheduledTaskBackgroundService<ScanMediaLibraries>>();

builder.Services.AddSingleton<IMigrationTask, MigrateFileMappings>();
builder.Services.AddSingleton<IMigrationBackupHook, ConfiguredMigrationBackupHook>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MigrationTaskRunner>();
builder.Services.AddScoped<MigrationAdministrationService>();

//Add download and store
builder.Services.AddScoped<IFileDownloadClient, RemoteTorrentDownloadClient>();
builder.Services.AddScoped<IFileStore, LocalFileStore>();

builder.Services.AddScoped<IFileDownloadClientProvider, FileDownloadClientProvider>();
builder.Services.AddScoped<IFileStoreProvider, FileStoreProvider>();
builder.Services.AddScoped<IFileExplorer, FileExplorer>();
builder.Services.AddScoped<IFileMapper, FileMapper>();
builder.Services.AddScoped<IMediaLibraryScanner, MediaLibraryScanner>();

//Add feed
builder.Services.AddSingleton<ISubscriptionFeedReader, MikananiSubscriptionFeedReader>();
builder.Services.AddSingleton<ISubscriptionReleaseMetadataExtractor, SubscriptionReleaseMetadataExtractor>();
builder.Services.AddSingleton<ISubscriptionAutomationMatcher, SubscriptionAutomationMatcher>();
builder.Services.AddScoped<ISubscriptionAutomationSimulationService, SubscriptionAutomationSimulationService>();
builder.Services.AddTransient<IFeedService, MikananiFeedService>();

//Add repositories
builder.Services.AddScoped<IAnimationInfoRepository, AnimationInfoRepository>();
builder.Services.AddScoped<IAnimationRepository, AnimationRepository>();
builder.Services.AddScoped<IAnimationGroupRepository, AnimationGroupRepository>();
builder.Services.AddScoped<IFeedRepository, FeedRepository>();
builder.Services.AddScoped<ISubscriptionAutomationPolicyRepository, SubscriptionAutomationPolicyRepository>();
builder.Services.AddScoped<ISeasonBangumiRepository, SeasonBangumiRepository>();
builder.Services.AddScoped<IBangumiSubgroupRepository, BangumiSubgroupRepository>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IChatActionRepository, ChatActionRepository>();
builder.Services.AddScoped<IFileMappingRepository, FileMappingRepository>();
builder.Services.AddScoped<IFileNameRegexRuleRepository, FileNameRegexRuleRepository>();
builder.Services.AddScoped<IMetadataReviewRepository, MetadataReviewRepository>();
builder.Services.AddScoped<IMigrationStateRepository, MigrationStateRepository>();
builder.Services.AddScoped<IMigrationLock, PostgreSqlMigrationLock>();
builder.Services.AddScoped<IWebDavTokenRepository, WebDavTokenRepository>();
builder.Services.AddScoped<IPlaybackRepository, PlaybackRepository>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<IMediaLibrarySourceRepository, MediaLibrarySourceRepository>();
builder.Services.AddSingleton<ISeasonScraper, MikananiSeasonScraper>();
builder.Services.AddScoped<IMetadataReviewService, MetadataReviewService>();
builder.Services.AddScoped<IIncidentRetryService, IncidentRetryService>();

//Add AI Inference
// Register all engines even when initially unconfigured. Runtime settings can then enable or
// switch an engine without rebuilding the service graph; the scheduled task reports disabled
// until the selected engine has the required endpoint/credential.
builder.Services.AddAIInference(builder.Configuration);
builder.Services.AddSingleton<InferAnimationMetadata>();
builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<InferAnimationMetadata>());
builder.Services.AddHostedService<ScheduledTaskBackgroundService<InferAnimationMetadata>>();

//Add Chat
builder.Services.AddChat();

//Add NFS (read-only NFSv4 export over the virtual filesystem)
// Always register NFS so a persisted setting can enable it before the host starts. Listener
// changes made while running are deliberately marked as requiring a restart.
builder.Services.AddNfs();

//Add SPA Hosting
builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

//Initialize Plugin
builder.InitializePlugin();

var app = builder.Build();

//Load Plugins
app.LoadPlugins();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();

app.MapControllers();
// The listener starts only after schema and blocking data migrations complete,
// so an unavailable endpoint is deliberately "not ready" during migration.
app.MapGet("/health/ready", () => Results.Text("ready", "text/plain"))
    .AllowAnonymous();

if (app.Environment.IsDevelopment())
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api") &&
                   !context.Request.Path.StartsWithSegments("/webdav") &&
                   !context.Request.Path.StartsWithSegments("/health"),
        then =>
        {
            then.UseSpa(config =>
            {
                var workingDirectory = Path.Combine(Directory.GetCurrentDirectory(),
                    "../SecondDimensionWatcherReDive.Client");
                config.UseAspSpaDevelopmentServer(
                    app.Lifetime,
                    "yarn",
                    "start",
                    workingDirectory,
                    new Dictionary<string, string>(),
                    TimeSpan.FromSeconds(15));
                config.Options.SourcePath = "../SecondDimensionWatcherReDive.Client";
                config.UseProxyToSpaDevelopmentServer("http://localhost:1234/");
            });
        });
}
else
{
    app.UseSpaStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.UseAuthorization();

if (app.Configuration.GetValue<bool?>("DisableCors") is true) app.UseCors("all");

// ConsoleLifetime is not running yet, so explicitly bridge startup SIGINT/SIGTERM
// into the migration token. Data-migration cancellation is persisted as failed.
using var startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(
    app.Lifetime.ApplicationStopping);
var migrationOptions = app.Services.GetRequiredService<IOptions<MigrationOptions>>().Value;
if (migrationOptions.Timeout <= TimeSpan.Zero)
    throw new InvalidOperationException("Migration:Timeout must be positive.");
using var migrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
    startupCancellation.Token);
migrationCancellation.CancelAfter(migrationOptions.Timeout);
ConsoleCancelEventHandler cancelStartup = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    startupCancellation.Cancel();
};
Console.CancelKeyPress += cancelStartup;
using var terminateRegistration = OperatingSystem.IsWindows()
    ? null
    : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        context.Cancel = true;
        startupCancellation.Cancel();
    });

try
{
    // One dedicated PostgreSQL session serializes both EF schema migrations and
    // resumable data migrations across all application replicas.
    await using var scope = app.Services.CreateAsyncScope();
    var migrationLock = scope.ServiceProvider.GetRequiredService<IMigrationLock>();
    await using var migrationLease = await migrationLock.AcquireAsync(migrationCancellation.Token);

    await using var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
    var runner = app.Services.GetRequiredService<MigrationTaskRunner>();
    if (migrationOptions.RequireBackup
        || !string.IsNullOrWhiteSpace(migrationOptions.BackupExecutable))
    {
        var pendingSchemaMigrations = await context.Database
            .GetPendingMigrationsAsync(migrationCancellation.Token);
        var hasPendingSchemaMigrations = pendingSchemaMigrations.Any();
        var hasPendingDataMigrations = !hasPendingSchemaMigrations
                                       && await runner.HasPendingAsync(migrationCancellation.Token);
        if (hasPendingSchemaMigrations || hasPendingDataMigrations)
            await app.Services.GetRequiredService<IMigrationBackupHook>()
                .ExecuteAsync(migrationCancellation.Token);
    }

    await context.Database.MigrateAsync(migrationCancellation.Token);

    // Database-backed configuration must be loaded before migration tasks and
    // hosted services resolve their options.
    await app.Services.GetRequiredService<IRuntimeSettingsInitializer>()
        .InitializeAsync(migrationCancellation.Token);

    // Blocking failures throw before Kestrel or any hosted service starts.
    await runner.RunAsync(migrationCancellation.Token);
}
catch (OperationCanceledException) when (startupCancellation.IsCancellationRequested)
{
    return;
}
catch (OperationCanceledException exception) when (migrationCancellation.IsCancellationRequested)
{
    throw new TimeoutException(
        $"Database migration exceeded the configured timeout of {migrationOptions.Timeout}.",
        exception);
}
finally
{
    Console.CancelKeyPress -= cancelStartup;
}

await app.RunAsync();
