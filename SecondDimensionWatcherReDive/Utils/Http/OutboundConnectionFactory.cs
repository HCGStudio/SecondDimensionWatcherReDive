using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Utils.Http;

internal interface IOutboundSocketConnector
{
    Task<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken);
}

internal sealed class OutboundSocketConnector : IOutboundSocketConnector
{
    public async Task<Stream> ConnectAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

internal sealed class OutboundConnectionFactory(
    OutboundAddressPolicy policy,
    IOutboundSocketConnector connector,
    IOptions<OutboundHttpOptions> options)
{
    public async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        var addresses = await policy.ResolveConnectionAddressesAsync(endpoint, cancellationToken);
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = TimeSpan.FromMilliseconds(options.Value.HappyEyeballsDelayMilliseconds);
        var attempts = addresses
            .Select((address, index) => ConnectAfterDelayAsync(
                address,
                endpoint.Port,
                delay * index,
                attemptCancellation.Token))
            .ToList();
        var failures = new List<Exception>(attempts.Count);

        while (attempts.Count > 0)
        {
            var completed = await Task.WhenAny(attempts);
            attempts.Remove(completed);
            try
            {
                var stream = await completed;
                await attemptCancellation.CancelAsync();
                ObserveAndDisposeLosers(attempts);
                return stream;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        throw new HttpRequestException(
            $"Unable to connect to any validated address for '{endpoint.Host}'.",
            new AggregateException(failures));
    }

    private async Task<Stream> ConnectAfterDelayAsync(
        IPAddress address,
        int port,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
        return await connector.ConnectAsync(address, port, cancellationToken);
    }

    private static void ObserveAndDisposeLosers(IEnumerable<Task<Stream>> attempts)
    {
        foreach (var attempt in attempts)
        {
            _ = attempt.ContinueWith(
                static completed =>
                {
                    if (completed.Status == TaskStatus.RanToCompletion)
                        completed.Result.Dispose();
                    else
                        _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
