using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Services;

public interface IMediaLibraryScanQueue
{
    bool Enqueue(Guid sourceId);

    bool IsQueuedOrRunning(Guid sourceId);
}

public sealed class MediaLibraryScanQueue : IMediaLibraryScanQueue
{
    internal const int Capacity = 256;
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    public bool Enqueue(Guid sourceId)
    {
        if (!_pending.TryAdd(sourceId, 0)) return false;
        if (_channel.Writer.TryWrite(sourceId)) return true;

        _pending.TryRemove(sourceId, out _);
        return false;
    }

    public bool IsQueuedOrRunning(Guid sourceId) => _pending.ContainsKey(sourceId);

    internal async IAsyncEnumerable<Guid> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var sourceId in _channel.Reader.ReadAllAsync(cancellationToken))
            yield return sourceId;
    }

    internal void Complete(Guid sourceId) => _pending.TryRemove(sourceId, out _);
}

public partial class MediaLibraryScanBackgroundService(
    MediaLibraryScanQueue queue,
    IServiceScopeFactory scopeFactory,
    IEnumerable<IScheduledTask> scheduledTasks,
    ILogger<MediaLibraryScanBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var sourceId in queue.ReadAllAsync(stoppingToken))
        {
            MediaLibraryScanResult? result = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scanner = scope.ServiceProvider.GetRequiredService<IMediaLibraryScanner>();
                result = await scanner.ScanAsync(sourceId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogScanFailed(logger, ex, sourceId);
            }
            finally
            {
                queue.Complete(sourceId);
            }

            if (result is { ImportedCount: > 0 })
            {
                scheduledTasks.FirstOrDefault(task =>
                        string.Equals(task.Id, "InferAnimationMetadata", StringComparison.Ordinal))
                    ?.Enqueue();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Media library source {SourceId} scan failed")]
    private static partial void LogScanFailed(ILogger logger, Exception exception, Guid sourceId);
}
