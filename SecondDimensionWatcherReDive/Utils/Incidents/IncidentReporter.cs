using System.Security.Cryptography;
using System.Text;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Utils.Incidents;

public sealed partial class IncidentReporter(
    IServiceScopeFactory scopeFactory,
    ILogger<IncidentReporter> logger,
    INotificationPublisher? notificationPublisher = null) : IIncidentReporter
{
    private const int MaxTitleLength = 256;
    private const int MaxDetailLength = 2048;
    private const int MaxSourceIdLength = 2048;

    public async Task<Incident> ReportAsync(
        IncidentReport report,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sourceId = Limit(report.SourceId, MaxSourceIdLength);
        var incident = new Incident(
            Guid.NewGuid(),
            CreateFingerprint(report.Type, sourceId),
            report.Type,
            report.Severity,
            Limit(report.Title, MaxTitleLength),
            Limit(report.Detail, MaxDetailLength),
            sourceId,
            now,
            now,
            null,
            0,
            null,
            null);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            var saved = await repository.UpsertAsync(incident, cancellationToken);
            if (notificationPublisher is not null)
            {
                var isDiskSpaceLow = saved.Type == IncidentType.DiskSpaceLow;
                var notificationType = isDiskSpaceLow
                    ? NotificationEventType.DiskSpaceLow
                    : NotificationEventType.IncidentOpened;
                var deduplicationKey =
                    $"{(isDiskSpaceLow ? "disk-space-low" : "incident-opened")}:{saved.Id}";
                // Occurrence 1 deliberately retains the pre-occurrence key so an
                // upgrade does not redeliver notifications already in the outbox.
                if (saved.Occurrence > 1)
                    deduplicationKey += $":{saved.Occurrence}";
                await notificationPublisher.PublishAsync(new NotificationEvent(
                    notificationType,
                    deduplicationKey,
                    saved.Title,
                    saved.Detail,
                    isDiskSpaceLow
                        ? "/incidents?type=diskSpaceLow"
                        : $"/incidents?focus={saved.Id}"), cancellationToken);
            }
            return saved;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogReportFailed(logger, ex, report.Type, sourceId);
            return incident;
        }
    }

    public async Task ResolveAsync(
        IncidentType type,
        string sourceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIncidentRepository>();
            await repository.ResolveByFingerprintAsync(
                CreateFingerprint(type, Limit(sourceId, MaxSourceIdLength)),
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogResolveFailed(logger, ex, type, sourceId);
        }
    }

    public static string CreateFingerprint(IncidentType type, string sourceId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)))
            .ToLowerInvariant();
        return $"{type.ToString().ToLowerInvariant()}:{hash}";
    }

    private static string Limit(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to persist incident {IncidentType} for source {SourceId}")]
    private static partial void LogReportFailed(
        ILogger logger,
        Exception exception,
        IncidentType incidentType,
        string sourceId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to resolve incident {IncidentType} for source {SourceId}")]
    private static partial void LogResolveFailed(
        ILogger logger,
        Exception exception,
        IncidentType incidentType,
        string sourceId);
}
