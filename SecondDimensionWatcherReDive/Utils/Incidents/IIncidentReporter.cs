using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.Incidents;

public sealed record IncidentReport(
    IncidentType Type,
    IncidentSeverity Severity,
    string Title,
    string Detail,
    string SourceId);

public interface IIncidentReporter
{
    Task<Incident> ReportAsync(IncidentReport report, CancellationToken cancellationToken);

    Task ResolveAsync(
        IncidentType type,
        string sourceId,
        CancellationToken cancellationToken);
}
