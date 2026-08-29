using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Services;

public sealed partial class IncidentReconciliationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IIncidentReporter incidentReporter,
    IIncidentDiskProbe diskProbe,
    IConfiguration configuration,
    ILogger<IncidentReconciliationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = configuration.GetValue<TimeSpan?>("Incidents:ReconciliationInterval")
                           ?? DefaultInterval;
            if (interval < TimeSpan.FromSeconds(10)) interval = TimeSpan.FromSeconds(10);
            await Task.Delay(interval, stoppingToken);
            await ReconcileSafelyAsync(stoppingToken);
        }
    }

    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var animationRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var incidentRepository = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();

        var failedInference = await animationRepository.GetFailedInferenceAsync(cancellationToken);
        var failedInferenceIds = failedInference
            .Select(info => info.Id.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var openAiIncidents = await incidentRepository.GetOpenAsync(
            IncidentType.AiFailure,
            cancellationToken);
        var openAiSourceIds = openAiIncidents
            .Select(incident => incident.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var info in failedInference.Where(info =>
                     !openAiSourceIds.Contains(info.Id.ToString())))
        {
            await incidentReporter.ReportAsync(new IncidentReport(
                    IncidentType.AiFailure,
                    IncidentSeverity.Error,
                    "AI metadata inference failed",
                    info.MetadataLastError ?? $"Inference failed after {info.AiRetryCount} attempts.",
                    info.Id.ToString()),
                cancellationToken);
        }

        foreach (var incident in openAiIncidents)
        {
            if (failedInferenceIds.Contains(incident.SourceId)) continue;
            if (!Guid.TryParse(incident.SourceId, out var infoId)) continue;
            var info = await animationRepository.FindByIdAsync(infoId, cancellationToken);
            // A queued retry is Pending and must remain visible until inference
            // actually succeeds. Deleted or successfully processed records recover.
            if (info is null || info.IsAiProcessed)
                await incidentReporter.ResolveAsync(
                    IncidentType.AiFailure,
                    incident.SourceId,
                    cancellationToken);
        }

        var missingMappings = await animationRepository
            .GetDownloadedWithoutFileMappingsAsync(cancellationToken);
        var openMappingIncidents = await incidentRepository.GetOpenAsync(
            IncidentType.FileMappingFailure,
            cancellationToken);
        var openMappingSourceIds = openMappingIncidents
            .Select(incident => incident.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var info in missingMappings.Where(info =>
                     !openMappingSourceIds.Contains(info.Id.ToString())))
        {
            await incidentReporter.ReportAsync(new IncidentReport(
                    IncidentType.FileMappingFailure,
                    IncidentSeverity.Error,
                    "Downloaded files are not mapped",
                    $"No virtual filesystem entries exist for '{info.Title}'.",
                    info.Id.ToString()),
                cancellationToken);
        }

        // Do not close an explicit remapping failure merely because old mappings
        // still exist. Inference remaps are replacement operations: on failure the
        // previous /unknown (or otherwise stale) rows intentionally remain. Every
        // successful mapping path resolves its own incident, so absence from the
        // zero-mapping detector is not proof of recovery.
        foreach (var incident in openMappingIncidents)
        {
            if (!Guid.TryParse(incident.SourceId, out var infoId)) continue;
            var info = await animationRepository.FindByIdAsync(infoId, cancellationToken);
            if (info is null || !info.IsDownloadFinished)
            {
                await incidentReporter.ResolveAsync(
                    IncidentType.FileMappingFailure,
                    incident.SourceId,
                    cancellationToken);
            }
        }

        // Stalled-download observations are runtime data, but a persisted incident
        // can be closed immediately after restart if its download already completed
        // or was cancelled while the service was offline.
        var downloadIncidents = await incidentRepository.GetOpenAsync(
            IncidentType.DownloadStalled,
            cancellationToken);
        foreach (var incident in downloadIncidents)
        {
            if (!Guid.TryParse(incident.SourceId, out var infoId)) continue;
            var info = await animationRepository.FindByIdAsync(infoId, cancellationToken);
            if (info is null || info.IsDownloadFinished || !info.IsDownloadTracked)
            {
                await incidentReporter.ResolveAsync(
                    IncidentType.DownloadStalled,
                    incident.SourceId,
                    cancellationToken);
            }
        }

        await diskProbe.ProbeAsync(cancellationToken);
    }

    private async Task ReconcileSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReconcileAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogReconciliationFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Incident reconciliation failed")]
    private static partial void LogReconciliationFailed(ILogger logger, Exception exception);
}
