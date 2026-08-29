using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Testcontainers.PostgreSql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.IntegrationTest.PostgreSql;

/// <summary>
/// Exercises the PostgreSQL-only repository surface against a migrated, disposable database.
/// Testcontainers owns container cleanup even when a test fails or the run is cancelled.
/// </summary>
[TestClass]
public sealed class FileMappingRepositoryPostgreSqlTests
{
    private static readonly PostgreSqlContainer Database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("sdw_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private static FileMappingRepositoryPostgreSqlTestFixture Fixture = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Fixture = new FileMappingRepositoryPostgreSqlTestFixture(Database.GetConnectionString());
        await Fixture.InitializeAsync(CancellationToken.None);
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        await Fixture.ResetAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task PrefixQuery_EscapesLikeWildcards_AndRootQueryUsesPostgreSqlRawSql()
    {
        var infoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        await Fixture.AddRangeAsync([
            Mapping(infoId, "/shows/100%_real/episode.mkv"),
            Mapping(infoId, "/shows/100xxreal/other.mkv")
        ], CancellationToken.None);

        var matches = await Fixture.GetByVirtualPathPrefixAsync(
            "/shows/100%_real", CancellationToken.None);
        var roots = await Fixture.GetRootEntriesAsync(CancellationToken.None);

        Assert.HasCount(1, matches);
        Assert.AreEqual("/shows/100%_real/episode.mkv", matches[0].VirtualPath);
        Assert.HasCount(1, roots);
        Assert.AreEqual("shows", roots[0].Name);
        Assert.IsTrue(roots[0].IsDirectory);
    }

    [TestMethod]
    public async Task ConcurrentWriters_AreSerialized_AndFailedTransactionRollsBack()
    {
        var firstInfoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        var secondInfoId = await Fixture.SeedDownloadedAnimationAsync(CancellationToken.None);
        const string CollidingPath = "/anime/shared.mkv";

        static async Task<bool> WriteAsync(FileMappingRepositoryPostgreSqlTestFixture fixture,
            FileMapping mapping)
        {
            try
            {
                await fixture.AddRangeAsync([mapping], CancellationToken.None);
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            WriteAsync(Fixture, Mapping(firstInfoId, CollidingPath)),
            WriteAsync(Fixture, Mapping(secondInfoId, CollidingPath)));

        Assert.AreEqual(1, results.Count(result => result));
        Assert.AreEqual(1, await Fixture.GetMappingCountAsync(CancellationToken.None));
        var versions = await Fixture.GetAnimationInfoStateVersionsAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new long[] { 0, 1 }, versions);
    }

    [TestMethod]
    public async Task DownloadCompletion_CommitsStateAndOneDurableJobTogether()
    {
        var seed = await Fixture.SeedTrackedAnimationAsync(CancellationToken.None);
        const string StorePath = "/store/completed";

        await Fixture.CompleteTrackedAnimationAsync(
            seed.ItemId, seed.AttemptId, StorePath, CancellationToken.None);
        await Fixture.CompleteTrackedAnimationAsync(
            seed.ItemId, seed.AttemptId, StorePath, CancellationToken.None);

        var state = await Fixture.GetCompletionStateAsync(
            seed.ItemId, CancellationToken.None);
        Assert.IsTrue(state.IsFinished);
        Assert.AreEqual(1, state.JobCount);
        Assert.AreEqual(seed.ItemId, state.Payload.ItemId);
        Assert.AreEqual(seed.AttemptId, state.Payload.DownloadAttemptId);
        Assert.AreEqual(StorePath, state.Payload.StorePath);
    }

    [TestMethod]
    public async Task DurableJobClaim_AllowsOnlyOneWorker()
    {
        var now = DateTimeOffset.UtcNow;
        var job = Job(DurableJobStatus.Pending, now);
        await Fixture.SeedDurableJobAsync(job, CancellationToken.None);

        var claims = await Task.WhenAll(
            Fixture.ClaimDueJobsAsync("worker-a", now, CancellationToken.None),
            Fixture.ClaimDueJobsAsync("worker-b", now, CancellationToken.None));

        Assert.AreEqual(1, claims.Sum(claim => claim.Count));
        Assert.AreEqual(job.Id, claims.SelectMany(claim => claim).Single().Id);
    }

    [TestMethod]
    public async Task DurableJobLease_RenewalProtectsSlowConsumerAndStillExpires()
    {
        var now = DateTimeOffset.UtcNow;
        var job = Job(DurableJobStatus.Pending, now);
        await Fixture.SeedDurableJobAsync(job, CancellationToken.None);
        Assert.HasCount(1, await Fixture.ClaimDueJobsAsync(
            "worker-a", now, CancellationToken.None));
        Assert.IsTrue(await Fixture.RenewDurableJobLeaseAsync(
            job.Id,
            "worker-a",
            now.AddSeconds(30),
            now.AddMinutes(2),
            CancellationToken.None));

        Assert.IsEmpty(await Fixture.ClaimDueJobsAsync(
            "worker-b", now.AddSeconds(61), CancellationToken.None));
        Assert.HasCount(1, await Fixture.ClaimDueJobsAsync(
            "worker-b", now.AddSeconds(121), CancellationToken.None));
    }

    [TestMethod]
    public async Task DurableJobLease_ExpiredOwnerCannotAdvanceStage()
    {
        var now = DateTimeOffset.UtcNow;
        var job = Job(DurableJobStatus.Pending, now);
        await Fixture.SeedDurableJobAsync(job, CancellationToken.None);
        Assert.HasCount(1, await Fixture.ClaimDueJobsAsync(
            "worker-a", now, CancellationToken.None));

        Assert.IsFalse(await Fixture.AdvanceDurableJobAsync(
            job.Id,
            "worker-a",
            DurableJobStage.MapFiles,
            DurableJobStage.Notify,
            now.AddMinutes(2),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ScheduledTaskLease_HasSingleOwnerAndExpiresForTakeover()
    {
        var now = DateTimeOffset.UtcNow;
        var first = await Fixture.TryAcquireTaskLeaseAsync(
            "SyncFeed", "instance-a", now, now.AddSeconds(30), false, CancellationToken.None);
        var overlapping = await Fixture.TryAcquireTaskLeaseAsync(
            "SyncFeed", "instance-b", now.AddSeconds(1), now.AddSeconds(31), false, CancellationToken.None);
        var takeover = await Fixture.TryAcquireTaskLeaseAsync(
            "SyncFeed", "instance-b", now.AddSeconds(31), now.AddSeconds(61), false, CancellationToken.None);

        Assert.IsTrue(first);
        Assert.IsFalse(overlapping);
        Assert.IsTrue(takeover);
    }

    [TestMethod]
    public async Task ScheduledTaskLease_NormalCompletionPreventsDuplicateUntilNextDueTime()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(await Fixture.TryAcquireTaskLeaseAsync(
            "ScrapeSeasonBangumi",
            "instance-a",
            now,
            now.AddSeconds(30),
            false,
            CancellationToken.None));
        await Fixture.CompleteTaskLeaseAsync(
            "ScrapeSeasonBangumi",
            "instance-a",
            now.AddSeconds(5),
            now.AddMinutes(10),
            CancellationToken.None);

        Assert.IsFalse(await Fixture.TryAcquireTaskLeaseAsync(
            "ScrapeSeasonBangumi",
            "instance-b",
            now.AddSeconds(31),
            now.AddMinutes(1),
            false,
            CancellationToken.None));
        Assert.IsTrue(await Fixture.TryAcquireTaskLeaseAsync(
            "ScrapeSeasonBangumi",
            "instance-b",
            now.AddMinutes(11),
            now.AddMinutes(12),
            false,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ScheduledTaskLease_ManualRunCanOverrideCompletedCooldown()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(await Fixture.TryAcquireTaskLeaseAsync(
            "InferAnimationMetadata",
            "instance-a",
            now,
            now.AddSeconds(30),
            false,
            CancellationToken.None));
        await Fixture.CompleteTaskLeaseAsync(
            "InferAnimationMetadata",
            "instance-a",
            now.AddSeconds(5),
            now.AddMinutes(30),
            CancellationToken.None);

        Assert.IsTrue(await Fixture.TryAcquireTaskLeaseAsync(
            "InferAnimationMetadata",
            "instance-b",
            now.AddSeconds(31),
            now.AddMinutes(1),
            true,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task DeadLetterJobs_CanBeRetriedOrMarkedHandled()
    {
        var now = DateTimeOffset.UtcNow;
        var retried = Job(DurableJobStatus.DeadLetter, now);
        var resolved = Job(DurableJobStatus.DeadLetter, now);
        await Fixture.SeedDurableJobAsync(retried, CancellationToken.None);
        await Fixture.SeedDurableJobAsync(resolved, CancellationToken.None);

        Assert.AreEqual(1, await Fixture.RetryJobsAsync(
            [retried.Id], now, CancellationToken.None));
        Assert.AreEqual(1, await Fixture.ResolveJobsAsync(
            [resolved.Id], now, CancellationToken.None));
        Assert.AreEqual(DurableJobStatus.Pending, await Fixture.GetJobStatusAsync(
            retried.Id, CancellationToken.None));
        Assert.AreEqual(DurableJobStatus.Resolved, await Fixture.GetJobStatusAsync(
            resolved.Id, CancellationToken.None));
    }

    private static FileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/" + Guid.NewGuid(), "local");

    private static DurableJob Job(DurableJobStatus status, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        return new DurableJob(
            id,
            $"test:{id:N}",
            DurableJobType.DownloadCompletion,
            status,
            DurableJobStage.MapFiles,
            JsonSerializer.Serialize(new DownloadCompletionJobPayload(
                Guid.NewGuid(), "/store", "local", Guid.NewGuid())),
            status == DurableJobStatus.DeadLetter ? 8 : 0,
            now,
            now,
            now,
            now,
            null,
            null,
            null,
            status == DurableJobStatus.DeadLetter ? "failed" : null);
    }
}
