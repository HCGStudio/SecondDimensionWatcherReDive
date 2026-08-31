using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
///     PostgreSQL integration-test boundary that keeps ApplicationContext usage in
///     the repository layer.
/// </summary>
internal sealed class MigrationStateRepositoryPostgreSqlTestFixture(string connectionString)
{
    private const string LegacyMigration = "20260418134549_AddMigrationMarkers";
    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions =
        new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString)
            .Options;

    public DateTimeOffset LegacyAppliedAt { get; } =
        new(2026, 4, 18, 13, 45, 49, TimeSpan.Zero);

    public async Task InitializeFromLegacyAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(LegacyMigration, cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"MigrationMarkers\" (\"Key\", \"AppliedAt\") VALUES ({"legacy"}, {LegacyAppliedAt})",
            cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task<MigrationExecution?> FindAsync(
        string key,
        int version,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new MigrationStateRepository(context);
        return await repository.FindAsync(key, version, cancellationToken);
    }

    public async Task<MigrationExecution> EnsurePendingAsync(
        string key,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new MigrationStateRepository(context);
        return await repository.EnsurePendingAsync(key, version, now, cancellationToken);
    }

    public async Task<MigrationExecution> MarkRunningAsync(
        string key,
        int version,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new MigrationStateRepository(context);
        return await repository.MarkRunningAsync(key, version, now, cancellationToken);
    }

    public async Task<MigrationExecution> SaveCheckpointAsync(
        string key,
        int version,
        string checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new MigrationStateRepository(context);
        return await repository.SaveCheckpointAsync(
            key,
            version,
            checkpoint,
            now,
            cancellationToken);
    }

    public async Task<MigrationExecution> MarkFailedAsync(
        string key,
        int version,
        string checkpoint,
        string error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new MigrationStateRepository(context);
        return await repository.MarkFailedAsync(
            key,
            version,
            checkpoint,
            error,
            now,
            cancellationToken);
    }

    public async Task<MigrationExecution> MarkCompletedAsync(
        string key,
        int version,
        string checkpoint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new MigrationStateRepository(context);
        return await repository.MarkCompletedAsync(
            key,
            version,
            checkpoint,
            now,
            cancellationToken);
    }

    public async Task<IMigrationLockLease> AcquireLockAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var migrationLock = new PostgreSqlMigrationLock(
            context,
            NullLogger<PostgreSqlMigrationLock>.Instance);
        return await migrationLock.AcquireAsync(cancellationToken);
    }

    public async Task SeedDownloadedAnimationAsync(
        Guid id,
        DateTimeOffset publishTime,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        context.AnimationInfo.Add(new Models.AnimationInfo
        {
            Id = id,
            Title = "migration cursor test",
            PublishTime = publishTime,
            IsDownloadFinished = true,
            FileStore = "local",
            StorePath = "/store/" + id.ToString("N"),
            IsAiProcessed = true
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetDownloadedMigrationBatchAsync(
        DateTimeOffset? beforePublishTime,
        Guid? beforeId,
        int take,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AnimationInfoRepository(context, _contextOptions);
        return await repository.GetDownloadedMigrationBatchAsync(
            beforePublishTime,
            beforeId,
            take,
            cancellationToken);
    }
}
