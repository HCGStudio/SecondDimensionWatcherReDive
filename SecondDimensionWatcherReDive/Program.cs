using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using AspSpaService;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SecondDimensionWatcherReDive;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Inference.AI;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Repositories;
using SecondDimensionWatcherReDive.Chat;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.MigrationTasks;
using SecondDimensionWatcherReDive.Utils.Feed;
using SecondDimensionWatcherReDive.Utils.FileDownload;
using SecondDimensionWatcherReDive.Utils.FileStore;
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
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
if (builder.Configuration["Config"] is { } configPath)
    builder.Configuration.AddYamlFile(configPath, optional: false, reloadOnChange: true);
var passwordFile = builder.Configuration["PasswordFile"] ?? "password.json";
builder.Configuration.AddJsonFile(passwordFile, optional: true, reloadOnChange: true);

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
});

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
builder.Services.AddHttpClient("RemoteTorrentDownloadClient", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Torrent:Remote:Url"]!);
    var overrideUserAgent = builder.Configuration["Torrent:Remote:UserAgent"];
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
contentTypeProvider.Mappings.Add(".mkv", "video/webm");
builder.Services.AddSingleton<IContentTypeProvider>(contentTypeProvider);

//Add channels
builder.Services.AddSingleton(Channel.CreateUnbounded<RemoteTorrentTrackRequest>());
builder.Services.AddSingleton(Channel.CreateUnbounded<FileDownloadStatus>());
builder.Services.AddSingleton(Channel.CreateUnbounded<DownloadCompleteRequest>());

//Add hosting services
builder.Services.AddHostedService<CompleteDownloadBackgroundService>();
builder.Services.AddHostedService<FetchRemoteTorrentBackgroundService>();
builder.Services.AddHostedService<UpdateDownloadStatusBackgroundService>();

//Add scheduled tasks
builder.Services.AddSingleton<SyncFeed>();
builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<SyncFeed>());
builder.Services.AddHostedService<ScheduledTaskBackgroundService<SyncFeed>>();

builder.Services.AddSingleton<ScrapeSeasonBangumi>();
builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<ScrapeSeasonBangumi>());
builder.Services.AddHostedService<ScheduledTaskBackgroundService<ScrapeSeasonBangumi>>();

builder.Services.AddSingleton<IMigrationTask, MigrateFileMappings>();
builder.Services.AddSingleton<MigrationTaskRunner>();

//Add download and store
builder.Services.AddScoped<IFileDownloadClient, RemoteTorrentDownloadClient>();
builder.Services.AddScoped<IFileStore, LocalFileStore>();

builder.Services.AddScoped<IFileDownloadClientProvider, FileDownloadClientProvider>();
builder.Services.AddScoped<IFileStoreProvider, FileStoreProvider>();
builder.Services.AddScoped<IFileExplorer, FileExplorer>();
builder.Services.AddScoped<IFileMapper, FileMapper>();

//Add feed
builder.Services.AddTransient<IFeedService, MikananiFeedService>();

//Add repositories
builder.Services.AddScoped<IAnimationInfoRepository, AnimationInfoRepository>();
builder.Services.AddScoped<IAnimationRepository, AnimationRepository>();
builder.Services.AddScoped<IAnimationGroupRepository, AnimationGroupRepository>();
builder.Services.AddScoped<IFeedRepository, FeedRepository>();
builder.Services.AddScoped<ISeasonBangumiRepository, SeasonBangumiRepository>();
builder.Services.AddScoped<IBangumiSubgroupRepository, BangumiSubgroupRepository>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IFileMappingRepository, FileMappingRepository>();
builder.Services.AddScoped<IMigrationMarkerRepository, MigrationMarkerRepository>();
builder.Services.AddSingleton<ISeasonScraper, MikananiSeasonScraper>();

//Add AI Inference
var aiProvider = builder.Configuration["AI:Provider"]
    is { Length: > 0 } p
    ? p
    : "OpenAI";
var aiApiKey = string.Equals(aiProvider, "Anthropic", StringComparison.OrdinalIgnoreCase)
    ? builder.Configuration["AI:Anthropic:ApiKey"]
    : builder.Configuration["AI:OpenAI:ApiKey"];
if (!string.IsNullOrEmpty(aiApiKey))
{
    builder.Services.AddAIInference(builder.Configuration);
    builder.Services.AddSingleton<InferAnimationMetadata>();
    builder.Services.AddSingleton<IScheduledTask>(sp => sp.GetRequiredService<InferAnimationMetadata>());
    builder.Services.AddHostedService<ScheduledTaskBackgroundService<InferAnimationMetadata>>();
}

//Add Chat
builder.Services.AddChat();

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

if (app.Environment.IsDevelopment())
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/api"),
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

// Run data migrations to completion before the host starts so that hosted
// services, scheduled tasks, and request handlers never observe a
// half-migrated database.
await app.Services.GetRequiredService<MigrationTaskRunner>().RunAsync(CancellationToken.None);

await app.RunAsync();
