namespace SecondDimensionWatcherReDive.Utils.Incidents;

public sealed record IncidentDiskProbeResult(
    bool IsHealthy,
    string Path,
    long AvailableBytes,
    long TotalBytes,
    string Detail);

public interface IIncidentDiskProbe
{
    Task<IncidentDiskProbeResult> ProbeAsync(CancellationToken cancellationToken);
}
