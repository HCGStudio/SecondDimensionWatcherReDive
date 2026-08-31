using Microsoft.EntityFrameworkCore;
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

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"AuthenticationStates\", \"FileMappings\", \"AnimationInfo\" RESTART IDENTITY CASCADE",
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

    public async Task<bool> TryClaimPasswordAsync(
        string passwordHash,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AuthenticationStateRepository(context);
        return await repository.TryClaimPasswordAsync(
            passwordHash,
            claimId,
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public async Task<string?> GetPasswordHashAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AuthenticationStateRepository(context);
        return await repository.GetPasswordHashAsync(cancellationToken);
    }
}
