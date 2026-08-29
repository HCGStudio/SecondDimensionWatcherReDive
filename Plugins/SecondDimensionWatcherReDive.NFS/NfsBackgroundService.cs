using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.NFS.Server;

namespace SecondDimensionWatcherReDive.NFS;

internal sealed partial class NfsBackgroundService(
    NfsTcpServer server,
    IOptions<NfsOptions> options,
    ILogger<NfsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // NFS is always registered so a database-backed runtime setting can enable it
        // before hosted services start. Listener changes still require a process restart.
        if (!options.Value.Enabled)
        {
            LogServerDisabled(logger);
            return;
        }

        try
        {
            await server.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogServerCrashed(logger, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "NFS server terminated unexpectedly")]
    private static partial void LogServerCrashed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "NFS server is disabled")]
    private static partial void LogServerDisabled(ILogger logger);
}
