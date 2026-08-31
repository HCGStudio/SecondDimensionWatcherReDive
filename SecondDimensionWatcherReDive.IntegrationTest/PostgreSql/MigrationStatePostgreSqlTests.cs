using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.Repositories;
using Testcontainers.PostgreSql;

namespace SecondDimensionWatcherReDive.IntegrationTest.PostgreSql;

[TestClass]
public sealed class MigrationStatePostgreSqlTests
{
    private static readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sdw_migration_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private static MigrationStateRepositoryPostgreSqlTestFixture Fixture = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Fixture = new MigrationStateRepositoryPostgreSqlTestFixture(Database.GetConnectionString());
        await Fixture.InitializeFromLegacyAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestMethod]
    public async Task Upgrade_PreservesLegacyCompletedMarkerAsVersionOne()
    {
        var state = await Fixture.FindAsync("legacy", 1, CancellationToken.None);

        Assert.IsNotNull(state);
        Assert.AreEqual(MigrationExecutionStatus.Completed, state.Status);
        Assert.AreEqual(1, state.AttemptCount);
        Assert.AreEqual(Fixture.LegacyAppliedAt, state.FinishedAt);
        Assert.AreEqual(Fixture.LegacyAppliedAt, state.UpdatedAt);
    }

    [TestMethod]
    public async Task FailedCheckpoint_CanResumeAndCompleteWithoutLosingAudit()
    {
        var key = "resume-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var pending = await Fixture.EnsurePendingAsync(key, 2, now, CancellationToken.None);
        Assert.AreEqual(MigrationExecutionStatus.Pending, pending.Status);
        Assert.AreEqual(0, pending.AttemptCount);

        await Fixture.MarkRunningAsync(key, 2, now.AddSeconds(1), CancellationToken.None);
        await Fixture.SaveCheckpointAsync(
            key,
            2,
            "batch-4",
            now.AddSeconds(2),
            CancellationToken.None);
        var failed = await Fixture.MarkFailedAsync(
            key,
            2,
            "batch-4",
            "simulated interruption",
            now.AddSeconds(3),
            CancellationToken.None);
        Assert.AreEqual(MigrationExecutionStatus.Failed, failed.Status);

        var resumed = await Fixture.MarkRunningAsync(
            key,
            2,
            now.AddSeconds(4),
            CancellationToken.None);
        Assert.AreEqual("batch-4", resumed.Checkpoint);
        Assert.AreEqual(2, resumed.AttemptCount);
        var completed = await Fixture.MarkCompletedAsync(
            key,
            2,
            "batch-9",
            now.AddSeconds(5),
            CancellationToken.None);

        Assert.AreEqual(MigrationExecutionStatus.Completed, completed.Status);
        Assert.AreEqual("batch-9", completed.Checkpoint);
        Assert.AreEqual("simulated interruption", completed.LastErrorSummary);
    }

    [TestMethod]
    public async Task AdvisoryLock_SerializesConcurrentInstances()
    {
        var first = await Fixture.AcquireLockAsync(CancellationToken.None);
        var secondAcquired = new TaskCompletionSource<IMigrationLockLease>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTask = Task.Run(async () =>
        {
            var lease = await Fixture.AcquireLockAsync(CancellationToken.None);
            secondAcquired.SetResult(lease);
        });

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        Assert.IsFalse(secondAcquired.Task.IsCompleted);

        await first.DisposeAsync();
        var second = await secondAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await second.DisposeAsync();
        await secondTask;
    }

    [TestMethod]
    public async Task AdvisoryLock_WaitCanBeCancelledDuringShutdown()
    {
        await using var first = await Fixture.AcquireLockAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => Fixture.AcquireLockAsync(cancellation.Token));
    }

    [TestMethod]
    public async Task DownloadedMigrationCursor_IsStableAcrossEqualPublishTimes()
    {
        var publishTime = DateTimeOffset.UtcNow.AddYears(-20);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
            await Fixture.SeedDownloadedAnimationAsync(
                id,
                publishTime,
                CancellationToken.None);

        var first = await Fixture.GetDownloadedMigrationBatchAsync(
            null,
            null,
            2,
            CancellationToken.None);
        var cursor = first[^1];
        var second = await Fixture.GetDownloadedMigrationBatchAsync(
            cursor.PublishTime,
            cursor.Id,
            2,
            CancellationToken.None);

        Assert.HasCount(2, first);
        Assert.HasCount(1, second);
        CollectionAssert.AreEquivalent(
            ids,
            first.Concat(second).Select(info => info.Id).ToArray());
    }
}
