using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Services;

public sealed partial class PostgresScheduledTaskLeaseManager(
    IServiceScopeFactory scopeFactory,
    ILogger<PostgresScheduledTaskLeaseManager> logger) : IScheduledTaskLeaseManager
{
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(10);

    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task<IScheduledTaskExecutionLease?> TryAcquireAsync(
        string taskId,
        TimeSpan interval,
        bool force,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IScheduledTaskLeaseRepository>();
        var acquired = await repository.TryAcquireAsync(
            taskId,
            _ownerId,
            now,
            now + LeaseDuration,
            force,
            cancellationToken);
        return acquired
            ? new ExecutionLease(taskId, _ownerId, interval, scopeFactory, logger)
            : null;
    }

    public async Task<IReadOnlyDictionary<string, ScheduledTaskStatus>> GetStatusesAsync(
        IReadOnlyCollection<string> taskIds,
        CancellationToken cancellationToken)
    {
        var ids = taskIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, ScheduledTaskStatus>(StringComparer.Ordinal);

        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IScheduledTaskLeaseRepository>();
        var persistedStates = await repository.GetStatesAsync(ids, cancellationToken);
        var statesById = persistedStates.ToDictionary(
            state => state.TaskId,
            StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        return ids.ToDictionary(
            taskId => taskId,
            taskId => statesById.TryGetValue(taskId, out var state)
                ? ToStatus(state, now)
                : new ScheduledTaskStatus(null, false),
            StringComparer.Ordinal);
    }

    private static ScheduledTaskStatus ToStatus(
        ScheduledTaskLeaseState state,
        DateTimeOffset now)
    {
        var isRunning = state.LeaseOwner is not null
                        && state.LeaseExpiresAt > now
                        && state.LastStartedAt is { } startedAt
                        && (state.LastCompletedAt is null
                            || startedAt > state.LastCompletedAt);
        return new ScheduledTaskStatus(state.LastCompletedAt, isRunning);
    }

    private sealed class ExecutionLease : IScheduledTaskExecutionLease
    {
        private readonly string _taskId;
        private readonly string _ownerId;
        private readonly TimeSpan _interval;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _renewalCancellation = new();
        private readonly CancellationTokenSource _leaseLost = new();
        private readonly Task _renewalTask;
        private bool _completed;

        public ExecutionLease(
            string taskId,
            string ownerId,
            TimeSpan interval,
            IServiceScopeFactory scopeFactory,
            ILogger logger)
        {
            _taskId = taskId;
            _ownerId = ownerId;
            _interval = interval;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _renewalTask = RenewLoopAsync();
        }

        public CancellationToken LeaseLostToken => _leaseLost.Token;

        public async Task CompleteAsync(
            bool succeeded,
            string? error,
            CancellationToken cancellationToken)
        {
            if (_completed) return;
            _completed = true;
            await StopRenewalAsync();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IScheduledTaskLeaseRepository>();
            var completedAt = DateTimeOffset.UtcNow;
            await repository.CompleteAsync(
                _taskId,
                _ownerId,
                completedAt,
                completedAt + _interval,
                succeeded,
                error,
                cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await StopRenewalAsync();
            _renewalCancellation.Dispose();
            _leaseLost.Dispose();
        }

        private async Task RenewLoopAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(RenewInterval);
                while (await timer.WaitForNextTickAsync(_renewalCancellation.Token))
                {
                    var now = DateTimeOffset.UtcNow;
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var repository = scope.ServiceProvider
                        .GetRequiredService<IScheduledTaskLeaseRepository>();
                    if (await repository.RenewAsync(
                            _taskId,
                            _ownerId,
                            now,
                            now + LeaseDuration,
                            _renewalCancellation.Token))
                        continue;

                    LogLeaseLost(_logger, _taskId);
                    _leaseLost.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (_renewalCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                LogLeaseRenewalFailed(_logger, exception, _taskId);
                _leaseLost.Cancel();
            }
        }

        private async Task StopRenewalAsync()
        {
            if (!_renewalCancellation.IsCancellationRequested)
                await _renewalCancellation.CancelAsync();
            await _renewalTask;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Scheduled task {TaskId} lost its execution lease")]
    private static partial void LogLeaseLost(ILogger logger, string taskId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Scheduled task {TaskId} lease renewal failed")]
    private static partial void LogLeaseRenewalFailed(
        ILogger logger,
        Exception exception,
        string taskId);
}
