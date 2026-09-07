using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Observability;

internal static class HealthTags
{
    public const string Ready = "ready";
}

internal sealed class DatabaseReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadinessRepository>();
        return await repository.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
    }
}

internal sealed class DistributedCacheReadinessHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await cache.GetAsync("health:ready", cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

internal sealed class QbittorrentReadinessHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient("RemoteTorrentDownloadClient");
        using var response = await client.GetAsync(
            "/api/v2/app/version",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
    }
}

internal sealed class LocalStorageReadinessHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(configuration["FileStore:Local"] ?? "./download");
        if (!Directory.Exists(path))
            return Task.FromResult(HealthCheckResult.Unhealthy());

        // Force a real filesystem operation without creating or deleting data.
        using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        _ = enumerator.MoveNext();
        return Task.FromResult(HealthCheckResult.Healthy());
    }
}

internal sealed class AiReadinessHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IAIEngine>();
        await engine.GetAvailableModelsAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

internal static class HealthResponseWriter
{
    public static async Task WriteAsync(
        HttpContext context,
        HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("status", report.Status.ToString().ToLowerInvariant());
        writer.WriteStartObject("checks");
        foreach (var entry in report.Entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(entry.Key);
            writer.WriteString("status", entry.Value.Status.ToString().ToLowerInvariant());
            writer.WriteNumber("durationMs", entry.Value.Duration.TotalMilliseconds);
            if (entry.Value.Exception is not null)
                writer.WriteString("errorType", entry.Value.Exception.GetType().Name);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted);
    }
}
