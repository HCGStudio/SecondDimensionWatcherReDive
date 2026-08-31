using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using AspSpaService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
using SecondDimensionWatcherReDive.Utils.Http;
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
var trustedProxyOptions = builder.Configuration
    .GetSection(TrustedProxyOptions.SectionName)
    .Get<TrustedProxyOptions>() ?? new TrustedProxyOptions();
builder.Services.AddOptions<TrustedProxyOptions>()
    .BindConfiguration(TrustedProxyOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        TrustedProxyConfiguration.IsValid,
        "ReverseProxy known proxies and networks must be valid IP addresses and CIDRs.")
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    TrustedProxyConfiguration.Apply(options, trustedProxyOptions));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimit:AuthPermitLimit", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("basic", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimit:BasicPermitLimit", 600),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimit:AiPermitLimit", 30),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

//Configure JWT
var jwtSecret = builder.Configuration["JwtSecret"] ??
                throw new ApplicationException("JwtSecret must be present in the config file.");
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32 ||
    jwtSecret.StartsWith("<Please fill", StringComparison.OrdinalIgnoreCase) ||
    jwtSecret.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
    throw new ApplicationException("JwtSecret must be replaced with at least 32 random bytes.");
var key = Encoding.UTF8.GetBytes(jwtSecret);
builder.Services.AddOptions<TokenSecurityOptions>()
    .BindConfiguration(TokenSecurityOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
var tokenSecurity = builder.Configuration
    .GetSection(TokenSecurityOptions.SectionName)
    .Get<TokenSecurityOptions>() ?? new TokenSecurityOptions();

var tokenValidationParams = new TokenValidationParameters
{
    ValidateIssuerSigningKey = true,
    RequireSignedTokens = true,
    IssuerSigningKey = new SymmetricSecurityKey(key),
    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    ValidateIssuer = true,
    ValidIssuer = tokenSecurity.Issuer,
    ValidateAudience = true,
    ValidAudience = tokenSecurity.Audience,
    ValidateLifetime = true,
    RequireExpirationTime = true,
    ClockSkew = TimeSpan.FromSeconds(30)
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
    var redisConnectionProvider = new RedisConnectionProvider(valkeyConnection);
    builder.Services.AddSingleton(redisConnectionProvider);
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConnectionMultiplexerFactory = () =>
            redisConnectionProvider.GetConnectionAsync(CancellationToken.None);
        options.InstanceName = builder.Configuration["Valkey:InstanceName"] ?? "sdw-redive:";
    });
    builder.Services.AddSingleton<IRefreshTokenStorage>(_ => new RedisRefreshTokenStorage(
        redisConnectionProvider,
        builder.Configuration["Valkey:InstanceName"] ?? "sdw-redive:"));
}
else
{
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IRefreshTokenStorage, MemoryRefreshTokenStorage>();
}
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RefreshTokenStore>();
builder.Services.AddSingleton<IDeviceTokenHasher>(_ => new DeviceTokenHasher(
    builder.Configuration["WebDavTokens:Pepper"] ??
    builder.Configuration["JwtSecret"]!));

//Configure HTTP client
builder.Services.AddOptions<QBittorrentRemoteOptions>()
    .BindConfiguration(QBittorrentRemoteOptions.SectionName);
builder.Services.AddOptions<OutboundHttpOptions>()
    .BindConfiguration(OutboundHttpOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
builder.Services.AddSingleton<OutboundAddressPolicy>();
builder.Services.AddSingleton<IOutboundSocketConnector, OutboundSocketConnector>();
builder.Services.AddSingleton<OutboundConnectionFactory>();
builder.Services.AddSingleton<ISafeOutboundHttpFetcher, SafeOutboundHttpFetcher>();
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

void ConfigureFeedClient(HttpClient client)
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
}

builder.Services.AddHttpClient("Feed", ConfigureFeedClient);
builder.Services.AddHttpClient("SafeFeed", client =>
{
    ConfigureFeedClient(client);
    // SafeOutboundHttpFetcher owns the total deadline so it can distinguish the
    // first-byte phase from bounded body streaming.
    client.Timeout = Timeout.InfiniteTimeSpan;
})
.ConfigurePrimaryHttpMessageHandler(serviceProvider =>
{
    var connectionFactory = serviceProvider.GetRequiredService<OutboundConnectionFactory>();
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<OutboundHttpOptions>>().Value;
    return new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
        MaxConnectionsPerServer = options.MaxConcurrentRequests,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseProxy = false,
        ConnectCallback = (context, cancellationToken) =>
            connectionFactory.ConnectAsync(context.DnsEndPoint, cancellationToken)
    };
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
builder.Services.AddSingleton<MigrationTaskRunner>();

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
builder.Services.AddScoped<IFileMappingRepository, FileMappingRepository>();
builder.Services.AddScoped<IFileNameRegexRuleRepository, FileNameRegexRuleRepository>();
builder.Services.AddScoped<IMetadataReviewRepository, MetadataReviewRepository>();
builder.Services.AddScoped<IMigrationMarkerRepository, MigrationMarkerRepository>();
builder.Services.AddScoped<IWebDavTokenRepository, WebDavTokenRepository>();
builder.Services.AddScoped<IPlaybackRepository, PlaybackRepository>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<IMediaLibrarySourceRepository, MediaLibrarySourceRepository>();
builder.Services.AddScoped<IAuthenticationStateRepository, AuthenticationStateRepository>();
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

app.UseForwardedHeaders();
app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api") &&
                   !context.Request.Path.StartsWithSegments("/webdav"),
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


await using (var scope = app.Services.CreateAsyncScope())
{
    await using var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
    await context.Database.MigrateAsync();
}

// Database-backed configuration must be loaded before migration tasks and hosted services
// resolve their options.
await app.Services.GetRequiredService<IRuntimeSettingsInitializer>()
    .InitializeAsync(CancellationToken.None);

// Run data migrations to completion before the host starts so that hosted
// services, scheduled tasks, and request handlers never observe a
// half-migrated database.
await app.Services.GetRequiredService<MigrationTaskRunner>().RunAsync(CancellationToken.None);

await app.RunAsync();
