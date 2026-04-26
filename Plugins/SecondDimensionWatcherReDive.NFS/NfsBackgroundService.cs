using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.NFS.Server;

namespace SecondDimensionWatcherReDive.NFS;

internal sealed partial class NfsBackgroundService(
    NfsTcpServer server,
    ILogger<NfsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
}
