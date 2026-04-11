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
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Inference.AI;
using SecondDimensionWatcherReDive.Models;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Plugin.FileRenamer;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.Feed;
using SecondDimensionWatcherReDive.Utils.FileDownload;
using SecondDimensionWatcherReDive.Utils.FileStore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Configuration.AddJsonFile("password.json", true, true);


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

builder.Services.AddMemoryCache();

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
builder.Services.AddHostedService<CompleteDownload>();
builder.Services.AddHostedService<FetchRemoteTorrent>();
builder.Services.AddHostedService<UpdateDownloadStatus>();
builder.Services.AddHostedService<SyncFeed>();

//Add download and store
builder.Services.AddScoped<IFileDownloadClient, RemoteTorrentDownloadClient>();
builder.Services.AddScoped<IFileStore, LocalFileStore>();
builder.Services.AddScoped<IFileOperator, LocalFileOperator>();

builder.Services.AddScoped<IFileDownloadClientProvider, FileDownloadClientProvider>();
builder.Services.AddScoped<IFileStoreProvider, FileStoreProvider>();

//Add feed
builder.Services.AddTransient<IFeedService, MikananiFeedService>();

//Add AI Inference
if (!string.IsNullOrEmpty(builder.Configuration["Inference:ApiKey"]))
{
    builder.Services.AddAIInference(builder.Configuration);
}

//Add File Renamer
builder.Services.AddFileRenamer();

//Add SPA Hosting
builder.Services.AddSpaStaticFiles(options =>
{
    options.RootPath = "wwwroot";
});

//Initialize Plugin
builder.InitializePlugin();

var app = builder.Build();

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
                var workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "../SecondDimensionWatcherReDive.Client");
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

await app.RunAsync();
