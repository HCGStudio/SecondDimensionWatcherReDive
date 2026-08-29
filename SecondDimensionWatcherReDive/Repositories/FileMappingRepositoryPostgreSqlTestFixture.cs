using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SecondDimensionWatcherReDive.Framework.DataRepository;

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

        var previousMigration = migrations[^2];
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
        var markerSurvived = await context.MigrationMarkers
            .AnyAsync(marker => marker.Key == MarkerKey, cancellationToken);
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
            "TRUNCATE TABLE \"FileMappings\", \"AnimationInfo\" RESTART IDENTITY CASCADE",
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
}

internal sealed record MigrationUpgradeResult(
    string PreviousMigration,
    string LatestMigration,
    string[] AppliedMigrations,
    bool MarkerSurvived,
    string ValuesJsonType,
    string[] CheckConstraints);
