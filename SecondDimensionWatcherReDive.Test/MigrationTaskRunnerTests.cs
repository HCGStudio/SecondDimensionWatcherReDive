using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.MigrationTasks;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class MigrationTaskRunnerTests
{
    [TestMethod]
    public async Task RunAsync_Success_PersistsCheckpointBeforeCompleted()
    {
        var repository = new InMemoryMigrationStateRepository();
        var migration = new DelegateMigration(async (context, token) =>
            await context.SaveCheckpointAsync("batch-1", token));
        using var provider = CreateProvider(repository);
        var runner = CreateRunner(provider, migration);

        var result = await runner.RunAsync(migration, CancellationToken.None);

        Assert.AreEqual(MigrationExecutionStatus.Completed, result.Status);
        Assert.AreEqual("batch-1", result.Checkpoint);
        CollectionAssert.AreEqual(
            new[] { "pending", "running", "checkpoint:batch-1", "completed" },
            repository.Transitions);
    }

    [TestMethod]
    public async Task RunAsync_ItemFailure_PersistsFailedAndNeverCompleted()
    {
        var repository = new InMemoryMigrationStateRepository();
        var migration = new DelegateMigration((_, _) =>
            throw new InvalidOperationException("broken item"));
        using var provider = CreateProvider(repository);
        var runner = CreateRunner(provider, migration);

        var exception = await Assert.ThrowsExactlyAsync<MigrationTaskFailedException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.AreEqual(migration.Key, exception.Key);
        var state = await repository.FindAsync(
            migration.Key,
            migration.Version,
            CancellationToken.None);
        Assert.IsNotNull(state);
        Assert.AreEqual(MigrationExecutionStatus.Failed, state.Status);
        StringAssert.Contains(state.LastErrorSummary, "broken item");
        CollectionAssert.DoesNotContain(repository.Transitions, "completed");
    }

    [TestMethod]
    public async Task RunAsync_Cancellation_UsesCleanupTokenAndLeavesFailedState()
    {
        var repository = new InMemoryMigrationStateRepository();
        using var cancellation = new CancellationTokenSource();
        var migration = new DelegateMigration((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        using var provider = CreateProvider(repository);
        var runner = CreateRunner(provider, migration);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => runner.RunAsync(cancellation.Token));

        var state = await repository.FindAsync(
            migration.Key,
            migration.Version,
            CancellationToken.None);
        Assert.IsNotNull(state);
        Assert.AreEqual(MigrationExecutionStatus.Failed, state.Status);
        StringAssert.Contains(state.LastErrorSummary, nameof(OperationCanceledException));
    }

    [TestMethod]
    public async Task RunAsync_InterruptedState_ResumesCheckpointAndIncrementsAttempt()
    {
        var repository = new InMemoryMigrationStateRepository();
        await repository.EnsurePendingAsync("test", 1, DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.MarkRunningAsync("test", 1, DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.SaveCheckpointAsync(
            "test",
            1,
            "batch-7",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        string? observedCheckpoint = null;
        var migration = new DelegateMigration((context, _) =>
        {
            observedCheckpoint = context.Checkpoint;
            return Task.CompletedTask;
        });
        using var provider = CreateProvider(repository);
        var runner = CreateRunner(provider, migration);

        var result = await runner.RunAsync(migration, CancellationToken.None);

        Assert.AreEqual("batch-7", observedCheckpoint);
        Assert.AreEqual(2, result.AttemptCount);
        Assert.AreEqual(MigrationExecutionStatus.Completed, result.Status);
    }

    [TestMethod]
    public async Task RunAsync_CompletedVersion_IsSkipped()
    {
        var repository = new InMemoryMigrationStateRepository();
        await repository.EnsurePendingAsync("test", 1, DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.MarkRunningAsync("test", 1, DateTimeOffset.UtcNow, CancellationToken.None);
        await repository.MarkCompletedAsync(
            "test",
            1,
            "done",
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        var executions = 0;
        var migration = new DelegateMigration((_, _) =>
        {
            executions++;
            return Task.CompletedTask;
        });
        using var provider = CreateProvider(repository);
        var runner = CreateRunner(provider, migration);

        var result = await runner.RunAsync(migration, CancellationToken.None);

        Assert.AreEqual(0, executions);
        Assert.AreEqual(MigrationExecutionStatus.Completed, result.Status);
        Assert.AreEqual(1, result.AttemptCount);
    }

    private static ServiceProvider CreateProvider(IMigrationStateRepository repository) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .BuildServiceProvider();

    private static MigrationTaskRunner CreateRunner(
        ServiceProvider provider,
        params IMigrationTask[] migrations) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        migrations,
        TimeProvider.System,
        NullLogger<MigrationTaskRunner>.Instance);

    private sealed class DelegateMigration(
        Func<MigrationExecutionContext, CancellationToken, Task> execute) : IMigrationTask
    {
        public string Key => "test";

        public int Version => 1;

        public MigrationFailurePolicy FailurePolicy => MigrationFailurePolicy.BlockStartup;

        public Task ExecuteAsync(
            MigrationExecutionContext context,
            CancellationToken cancellationToken) => execute(context, cancellationToken);
    }

    private sealed class InMemoryMigrationStateRepository : IMigrationStateRepository
    {
        private readonly Dictionary<(string Key, int Version), MigrationExecution> _states = [];

        public List<string> Transitions { get; } = [];

        public Task<MigrationExecution?> FindAsync(
            string key,
            int version,
            CancellationToken cancellationToken)
        {
            _states.TryGetValue((key, version), out var state);
            return Task.FromResult(state);
        }

        public Task<IReadOnlyList<MigrationExecution>> GetAllAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MigrationExecution>>(_states.Values.ToList());

        public Task<MigrationExecution> EnsurePendingAsync(
            string key,
            int version,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (!_states.TryGetValue((key, version), out var state))
            {
                state = new MigrationExecution(
                    key,
                    version,
                    MigrationExecutionStatus.Pending,
                    null,
                    null,
                    null,
                    now,
                    0,
                    null);
                _states.Add((key, version), state);
                Transitions.Add("pending");
            }
            return Task.FromResult(state);
        }

        public Task<MigrationExecution> MarkRunningAsync(
            string key,
            int version,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var current = _states[(key, version)];
            var state = current with
            {
                Status = MigrationExecutionStatus.Running,
                StartedAt = now,
                FinishedAt = null,
                UpdatedAt = now,
                AttemptCount = current.AttemptCount + 1
            };
            _states[(key, version)] = state;
            Transitions.Add("running");
            return Task.FromResult(state);
        }

        public Task<MigrationExecution> SaveCheckpointAsync(
            string key,
            int version,
            string? checkpoint,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var state = _states[(key, version)] with
            {
                Checkpoint = checkpoint,
                UpdatedAt = now
            };
            _states[(key, version)] = state;
            Transitions.Add($"checkpoint:{checkpoint}");
            return Task.FromResult(state);
        }

        public Task<MigrationExecution> MarkCompletedAsync(
            string key,
            int version,
            string? checkpoint,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var state = _states[(key, version)] with
            {
                Status = MigrationExecutionStatus.Completed,
                Checkpoint = checkpoint,
                FinishedAt = now,
                UpdatedAt = now
            };
            _states[(key, version)] = state;
            Transitions.Add("completed");
            return Task.FromResult(state);
        }

        public Task<MigrationExecution> MarkFailedAsync(
            string key,
            int version,
            string? checkpoint,
            string errorSummary,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var state = _states[(key, version)] with
            {
                Status = MigrationExecutionStatus.Failed,
                Checkpoint = checkpoint,
                LastErrorSummary = errorSummary,
                FinishedAt = now,
                UpdatedAt = now
            };
            _states[(key, version)] = state;
            Transitions.Add("failed");
            return Task.FromResult(state);
        }
    }
}
