using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed partial class NfsTcpServer(
    IServiceScopeFactory scopeFactory,
    IOptions<NfsOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<NfsTcpServer> logger)
{
    private TcpListener? _listener;

    public int BoundPort { get; private set; }

    public void Bind()
    {
        if (_listener is not null)
            return;

        var opts = options.Value;
        if (!IPAddress.TryParse(opts.BindAddress, out var address))
            throw new InvalidOperationException(
                $"Invalid Nfs:BindAddress '{opts.BindAddress}'");

        var listener = new TcpListener(address, opts.Port);
        listener.Start();
        _listener = listener;
        BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        LogServerStarted(logger, address.ToString(), BoundPort);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Bind();
        var listener = _listener!;

        var opts = options.Value;
        using var connectionLimit = new SemaphoreSlim(opts.MaxConnections);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await connectionLimit.WaitAsync(cancellationToken);

                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch
                {
                    connectionLimit.Release();
                    throw;
                }

                _ = Task.Run(
                    () => HandleClientAsync(client, connectionLimit, cancellationToken),
                    cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
            _listener = null;
            BoundPort = 0;
            LogServerStopped(logger);
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        SemaphoreSlim connectionLimit,
        CancellationToken cancellationToken)
    {
        try
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var handler = new NfsConnectionHandler(
                scopeFactory,
                loggerFactory.CreateLogger<NfsConnectionHandler>());
            await handler.RunAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogClientFailed(logger, ex);
        }
        finally
        {
            client.Dispose();
            connectionLimit.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "NFS server listening on {Address}:{Port}")]
    private static partial void LogServerStarted(ILogger logger, string address, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "NFS server stopped")]
    private static partial void LogServerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "NFS client handler crashed")]
    private static partial void LogClientFailed(ILogger logger, Exception ex);
}
