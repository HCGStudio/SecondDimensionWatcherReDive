using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.Feed;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Owns PostgreSQL setup and inspection for repository integration tests without
/// exposing the EF context outside the repository implementation boundary.
/// </summary>
internal sealed class FileMappingRepositoryPostgreSqlTestFixture(string connectionString)
{
    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions =
        new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString)
            .Options;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task<MigrationUpgradeResult> UpgradeFromPreviousMigrationAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var migrations = context.Database.GetMigrations().ToArray();
        if (migrations.Length < 2)
            throw new InvalidOperationException("At least two migrations are required for an upgrade test.");

        var previousMigration = migrations.Single(migration =>
            migration.EndsWith("_AddApplicationSettings", StringComparison.Ordinal));
        var latestMigration = migrations[^1];

        await context.Database.EnsureDeletedAsync(cancellationToken);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration, cancellationToken);

        const string MarkerKey = "postgres-upgrade-sentinel";
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"MigrationMarkers\" (\"Key\", \"AppliedAt\") VALUES ({0}, {1})",
            [MarkerKey, DateTimeOffset.UtcNow],
            cancellationToken);

        await migrator.MigrateAsync(latestMigration, cancellationToken);

        var appliedMigrations = await context.Database
            .GetAppliedMigrationsAsync(cancellationToken);
        var markerSurvived = await context.MigrationStates
            .AnyAsync(
                marker => marker.Key == MarkerKey &&
                    marker.Version == 1 &&
                    marker.Status == MigrationExecutionStatus.Completed,
                cancellationToken);
        var valuesJsonType = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT data_type AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'ApplicationSettings'
                  AND column_name = 'ValuesJson'
                """)
            .SingleAsync(cancellationToken);
        var checkConstraints = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT constraint_name AS "Value"
                FROM information_schema.table_constraints
                WHERE table_schema = 'public'
                  AND table_name = 'ApplicationSettings'
                  AND constraint_type = 'CHECK'
                ORDER BY constraint_name
                """)
            .ToArrayAsync(cancellationToken);

        return new MigrationUpgradeResult(
            previousMigration,
            latestMigration,
            appliedMigrations.ToArray(),
            markerSurvived,
            valuesJsonType,
            checkConstraints);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"DurableJobs\", \"ScheduledTaskStates\", \"FileMappings\", \"AnimationInfo\" RESTART IDENTITY CASCADE",
            cancellationToken);
    }

    public async Task<Guid> SeedDownloadedAnimationAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var info = new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = "integration test",
            IsDownloadFinished = true,
            FileStore = "local",
            StorePath = "/store/" + Guid.NewGuid().ToString("N")
        };
        context.AnimationInfo.Add(info);
        await context.SaveChangesAsync(cancellationToken);
        return info.Id;
    }

    public async Task AddRangeAsync(
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        await repository.AddRangeAsync(mappings, cancellationToken);
    }

    public async Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(
        string virtualPathPrefix,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.GetByVirtualPathPrefixAsync(virtualPathPrefix, cancellationToken);
    }

    public async Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.GetRootEntriesAsync(cancellationToken);
    }

    public async Task<int> GetMappingCountAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.FileMappings.CountAsync(cancellationToken);
    }

    public async Task<long[]> GetAnimationInfoStateVersionsAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.AnimationInfo
            .OrderBy(info => info.Id)
            .Select(info => info.StateVersion)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<(Guid ItemId, Guid AttemptId)> SeedTrackedAnimationAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var attemptId = Guid.NewGuid();
        var info = new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = "tracked integration test",
            IsDownloadTracked = true,
            DownloadAttemptId = attemptId,
            AutomationDisposition = SubscriptionAutomationDisposition.ManualDownloadQueued
        };
        context.AnimationInfo.Add(info);
        await context.SaveChangesAsync(cancellationToken);
        return (info.Id, attemptId);
    }

    public async Task CompleteTrackedAnimationAsync(
        Guid itemId,
        Guid attemptId,
        string storePath,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AnimationInfoRepository(context, _contextOptions,
            new SubscriptionAutomationMatcher(new SubscriptionReleaseMetadataExtractor()));
        var result = await repository.TryCompleteDownloadAsync(
            itemId,
            attemptId,
            "local",
            storePath,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (result is null)
            throw new InvalidOperationException("The tracked download was not completed.");
    }

    public async Task<(bool IsFinished, int JobCount, DownloadCompletionJobPayload Payload)>
        GetCompletionStateAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var finished = await context.AnimationInfo
            .Where(info => info.Id == itemId)
            .Select(info => info.IsDownloadFinished)
            .SingleAsync(cancellationToken);
        var jobs = await context.DurableJobs
            .Where(job => job.Type == DurableJobType.DownloadCompletion)
            .ToListAsync(cancellationToken);
        var payload = System.Text.Json.JsonSerializer
            .Deserialize<DownloadCompletionJobPayload>(jobs.Single().PayloadJson)!;
        return (finished, jobs.Count, payload);
    }

    public async Task SeedDurableJobAsync(
        DurableJob job,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        context.DurableJobs.Add(new Models.DurableJob
        {
            Id = job.Id,
            DeduplicationKey = job.DeduplicationKey,
            Type = job.Type,
            Status = job.Status,
            Stage = job.Stage,
            PayloadJson = job.PayloadJson,
            AttemptCount = job.AttemptCount,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            NextAttemptAt = job.NextAttemptAt,
            LastAttemptAt = job.LastAttemptAt,
            CompletedAt = job.CompletedAt,
            LeaseOwner = job.LeaseOwner,
            LeaseExpiresAt = job.LeaseExpiresAt,
            LastError = job.LastError
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DurableJob>> ClaimDueJobsAsync(
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new DurableJobRepository(context);
        return await repository.ClaimDueAsync(
            ownerId,
            now,
            now.AddMinutes(1),
            10,
            cancellationToken);
    }

    public async Task<bool> RenewDurableJobLeaseAsync(
        Guid id,
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new DurableJobRepository(context).RenewLeaseAsync(
            id,
            ownerId,
            now,
            leaseUntil,
            cancellationToken);
    }

    public async Task<bool> AdvanceDurableJobAsync(
        Guid id,
        string ownerId,
        DurableJobStage expectedStage,
        DurableJobStage nextStage,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new DurableJobRepository(context).AdvanceStageAsync(
            id,
            ownerId,
            expectedStage,
            nextStage,
            now,
            cancellationToken);
    }

    public async Task<bool> TryAcquireTaskLeaseAsync(
        string taskId,
        string ownerId,
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        bool force,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new ScheduledTaskLeaseRepository(context);
        return await repository.TryAcquireAsync(
            taskId,
            ownerId,
            now,
            leaseUntil,
            force,
            cancellationToken);
    }

    public async Task CompleteTaskLeaseAsync(
        string taskId,
        string ownerId,
        DateTimeOffset completedAt,
        DateTimeOffset nextRunAt,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await new ScheduledTaskLeaseRepository(context).CompleteAsync(
            taskId,
            ownerId,
            completedAt,
            nextRunAt,
            true,
            null,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduledTaskLeaseState>> GetTaskLeaseStatesAsync(
        IReadOnlyCollection<string> taskIds,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ScheduledTaskLeaseRepository(context).GetStatesAsync(
            taskIds,
            cancellationToken);
    }

    public async Task<int> RetryJobsAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new DurableJobRepository(context)
            .RetryAsync(ids, now, cancellationToken);
    }

    public async Task<int> ResolveJobsAsync(
        IReadOnlyCollection<Guid> ids,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new DurableJobRepository(context)
            .ResolveAsync(ids, now, cancellationToken);
    }

    public async Task<DurableJobStatus> GetJobStatusAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.DurableJobs
            .Where(job => job.Id == id)
            .Select(job => job.Status)
            .SingleAsync(cancellationToken);
    }
}

internal sealed record MigrationUpgradeResult(
    string PreviousMigration,
    string LatestMigration,
    string[] AppliedMigrations,
    bool MarkerSurvived,
    string ValuesJsonType,
    string[] CheckConstraints);
