using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.Incidents;

public sealed class IncidentDiskProbe(
    IConfiguration configuration,
    IIncidentReporter incidentReporter) : IIncidentDiskProbe
{
    private const long DefaultMinimumAvailableBytes = 5L * 1024 * 1024 * 1024;
    private const double DefaultMinimumAvailablePercent = 5;

    public async Task<IncidentDiskProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(configuration["FileStore:Local"] ?? "./download");

        try
        {
            var drive = FindDrive(path)
                        ?? throw new IOException($"No mounted volume contains '{path}'.");
            var availableBytes = drive.AvailableFreeSpace;
            var totalBytes = drive.TotalSize;
            var availablePercent = totalBytes <= 0
                ? 0
                : availableBytes * 100d / totalBytes;
            var minimumBytes = Math.Max(0,
                configuration.GetValue<long?>("Incidents:Disk:MinimumAvailableBytes")
                ?? DefaultMinimumAvailableBytes);
            var minimumPercent = Math.Clamp(
                configuration.GetValue<double?>("Incidents:Disk:MinimumAvailablePercent")
                ?? DefaultMinimumAvailablePercent,
                0,
                100);
            var healthy = availableBytes >= minimumBytes && availablePercent >= minimumPercent;
            var detail = healthy
                ? $"{FormatBytes(availableBytes)} available ({availablePercent:F1}%)."
                : $"Only {FormatBytes(availableBytes)} available ({availablePercent:F1}%); " +
                  $"required at least {FormatBytes(minimumBytes)} and {minimumPercent:F1}%.";

            if (healthy)
            {
                await incidentReporter.ResolveAsync(
                    IncidentType.DiskSpaceLow,
                    path,
                    cancellationToken);
            }
            else
            {
                await incidentReporter.ReportAsync(new IncidentReport(
                        IncidentType.DiskSpaceLow,
                        availablePercent < 2 ? IncidentSeverity.Critical : IncidentSeverity.Error,
                        "Storage space is running low",
                        detail,
                        path),
                    cancellationToken);
            }

            return new IncidentDiskProbeResult(
                healthy,
                path,
                availableBytes,
                totalBytes,
                detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var detail = $"Unable to inspect storage volume: {ex.Message}";
            await incidentReporter.ReportAsync(new IncidentReport(
                    IncidentType.DiskSpaceLow,
                    IncidentSeverity.Error,
                    "Storage volume cannot be inspected",
                    detail,
                    path),
                cancellationToken);
            return new IncidentDiskProbeResult(false, path, 0, 0, detail);
        }
    }

    private static DriveInfo? FindDrive(string path)
    {
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Where(drive => IsWithin(path, drive.RootDirectory.FullName))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
            .FirstOrDefault();
    }

    private static bool IsWithin(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal)) return true;
        var prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string FormatBytes(long bytes)
    {
        var gib = bytes / (1024d * 1024 * 1024);
        return $"{gib:F1} GiB";
    }
}
