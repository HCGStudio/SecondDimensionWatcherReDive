using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ScheduledTaskBaseTests
{
    [TestMethod]
    public async Task RunNowAsync_ConcurrentRequestsAreCoalesced()
    {
        var task = new BlockingTask();
        var leaseManager = new FakeLeaseManager();
        using var cancellation = new CancellationTokenSource();
        var processor = task.ProcessQueueAsync(leaseManager, cancellation.Token);

        var first = task.RunNowAsync(CancellationToken.None);
        await task.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = task.RunNowAsync(CancellationToken.None);
        task.Release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(1, task.ExecutionCount);
        Assert.AreEqual(1, leaseManager.AcquireCount);
        Assert.AreEqual(1, leaseManager.Lease.CompletionCount);

        await cancellation.CancelAsync();
        await AssertCanceledAsync(processor);
    }

    [TestMethod]
    public async Task RunNowAsync_LeaseOwnedByAnotherInstance_SkipsExecution()
    {
        var task = new BlockingTask();
        var leaseManager = new FakeLeaseManager { Deny = true };
        using var cancellation = new CancellationTokenSource();
        var processor = task.ProcessQueueAsync(leaseManager, cancellation.Token);

        await task.RunNowAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(0, task.ExecutionCount);
        Assert.AreEqual(1, leaseManager.AcquireCount);
        Assert.IsTrue(leaseManager.LastForce);

        await cancellation.CancelAsync();
        await AssertCanceledAsync(processor);
    }

    [TestMethod]
    public async Task RunScheduledAsync_ContentionReportsSkippedWithoutForcingCooldown()
    {
        var task = new BlockingTask();
        var leaseManager = new FakeLeaseManager { Deny = true };
        using var cancellation = new CancellationTokenSource();
        var processor = task.ProcessQueueAsync(leaseManager, cancellation.Token);

        var executed = await task.RunScheduledAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsFalse(executed);
        Assert.IsFalse(leaseManager.LastForce);
        await cancellation.CancelAsync();
        await AssertCanceledAsync(processor);
    }

    [TestMethod]
    public async Task RunScheduledAsync_ForceArrivingDuringAcquisitionRetriesAsForced()
    {
        var task = new BlockingTask();
        var leaseManager = new ForceUpgradeLeaseManager();
        using var cancellation = new CancellationTokenSource();
        var processor = task.ProcessQueueAsync(leaseManager, cancellation.Token);

        var scheduled = task.RunScheduledAsync(CancellationToken.None);
        await leaseManager.FirstAcquireStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        task.Enqueue();
        leaseManager.ReleaseFirstAcquire.TrySetResult();
        await task.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        task.Release.TrySetResult();

        Assert.IsTrue(await scheduled.WaitAsync(TimeSpan.FromSeconds(2)));
        CollectionAssert.AreEqual(new[] { false, true }, leaseManager.Forces);
        Assert.AreEqual(1, task.ExecutionCount);

        await cancellation.CancelAsync();
        await AssertCanceledAsync(processor);
    }

    [TestMethod]
    public async Task RunScheduledAsync_TemporaryLeaseStoreFailureDoesNotStopQueue()
    {
        var task = new BlockingTask();
        var leaseManager = new FakeLeaseManager
        {
            AcquireException = new InvalidOperationException("database unavailable")
        };
        using var cancellation = new CancellationTokenSource();
        var processor = task.ProcessQueueAsync(leaseManager, cancellation.Token);

        var exception = await Assert.ThrowsExactlyAsync<ScheduledTaskLeaseUnavailableException>(
            () => task.RunScheduledAsync(CancellationToken.None));
        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);

        leaseManager.AcquireException = null;
        leaseManager.Deny = true;
        Assert.IsFalse(await task.RunScheduledAsync(CancellationToken.None));
        await cancellation.CancelAsync();
        await AssertCanceledAsync(processor);
    }

    [TestMethod]
    public void MediaLibraryQueue_RejectsItemsBeyondCapacity()
    {
        var queue = new MediaLibraryScanQueue();
        var accepted = Enumerable.Range(0, MediaLibraryScanQueue.Capacity)
            .Select(_ => queue.Enqueue(Guid.NewGuid()))
            .ToList();

        Assert.IsTrue(accepted.All(value => value));
        Assert.IsFalse(queue.Enqueue(Guid.NewGuid()));
    }

    private sealed class BlockingTask : ScheduledTaskBase
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecutionCount { get; private set; }
        public override string Id => "test";
        public override TimeSpan Interval => TimeSpan.FromMinutes(1);

        protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
        {
            ExecutionCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private static async Task AssertCanceledAsync(Task task)
    {
        try
        {
            await task;
            Assert.Fail("Expected the queue processor to be cancelled.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class FakeLeaseManager : IScheduledTaskLeaseManager
    {
        public FakeLease Lease { get; } = new();
        public bool Deny { get; set; }
        public Exception? AcquireException { get; set; }
        public int AcquireCount { get; private set; }
        public bool LastForce { get; private set; }

        public Task<IScheduledTaskExecutionLease?> TryAcquireAsync(
            string taskId,
            TimeSpan interval,
            bool force,
            CancellationToken cancellationToken)
        {
            AcquireCount++;
            LastForce = force;
            if (AcquireException is not null)
                return Task.FromException<IScheduledTaskExecutionLease?>(AcquireException);
            return Task.FromResult<IScheduledTaskExecutionLease?>(Deny ? null : Lease);
        }

        public Task<IReadOnlyDictionary<string, ScheduledTaskStatus>> GetStatusesAsync(
            IReadOnlyCollection<string> taskIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ScheduledTaskStatus>>(
                new Dictionary<string, ScheduledTaskStatus>());
    }

    private sealed class ForceUpgradeLeaseManager : IScheduledTaskLeaseManager
    {
        public TaskCompletionSource FirstAcquireStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstAcquire { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public bool[] Forces => _forces.ToArray();

        private readonly List<bool> _forces = [];
        private readonly FakeLease _lease = new();

        public async Task<IScheduledTaskExecutionLease?> TryAcquireAsync(
            string taskId,
            TimeSpan interval,
            bool force,
            CancellationToken cancellationToken)
        {
            _forces.Add(force);
            if (_forces.Count == 1)
            {
                FirstAcquireStarted.TrySetResult();
                await ReleaseFirstAcquire.Task.WaitAsync(cancellationToken);
                return null;
            }

            return _lease;
        }

        public Task<IReadOnlyDictionary<string, ScheduledTaskStatus>> GetStatusesAsync(
            IReadOnlyCollection<string> taskIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, ScheduledTaskStatus>>(
                new Dictionary<string, ScheduledTaskStatus>());
    }

    private sealed class FakeLease : IScheduledTaskExecutionLease
    {
        public int CompletionCount { get; private set; }
        public CancellationToken LeaseLostToken => CancellationToken.None;

        public Task CompleteAsync(
            bool succeeded,
            string? error,
            CancellationToken cancellationToken)
        {
            CompletionCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
