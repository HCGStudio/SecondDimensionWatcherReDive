using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Repositories;
using Testcontainers.PostgreSql;
using DomainFileMapping = SecondDimensionWatcherReDive.Framework.DataRepository.FileMapping;
using ApplicationContext = SecondDimensionWatcherReDive.Models.ApplicationContext;
using AnimationInfoEntity = SecondDimensionWatcherReDive.Models.AnimationInfo;

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

    private static DbContextOptions<ApplicationContext> _options = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        _options = new DbContextOptionsBuilder<ApplicationContext>()
            .UseNpgsql(Database.GetConnectionString())
            .Options;
        await using var context = new ApplicationContext(_options);
        await context.Database.MigrateAsync();
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        await using var context = new ApplicationContext(_options);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"FileMappings\", \"AnimationInfo\" RESTART IDENTITY CASCADE");
    }

    [TestMethod]
    public async Task PrefixQuery_EscapesLikeWildcards_AndRootQueryUsesPostgreSqlRawSql()
    {
        var info = await SeedDownloadedAnimationAsync();
        await using var context = new ApplicationContext(_options);
        var repository = new FileMappingRepository(context, _options);
        await repository.AddRangeAsync([
            Mapping(info.Id, "/shows/100%_real/episode.mkv"),
            Mapping(info.Id, "/shows/100xxreal/other.mkv")
        ], CancellationToken.None);

        var matches = await repository.GetByVirtualPathPrefixAsync(
            "/shows/100%_real", CancellationToken.None);
        var roots = await repository.GetRootEntriesAsync(CancellationToken.None);

        Assert.HasCount(1, matches);
        Assert.AreEqual("/shows/100%_real/episode.mkv", matches[0].VirtualPath);
        Assert.HasCount(1, roots);
        Assert.AreEqual("shows", roots[0].Name);
        Assert.IsTrue(roots[0].IsDirectory);
    }

    [TestMethod]
    public async Task ConcurrentWriters_AreSerialized_AndFailedTransactionRollsBack()
    {
        var firstInfo = await SeedDownloadedAnimationAsync();
        var secondInfo = await SeedDownloadedAnimationAsync();
        const string CollidingPath = "/anime/shared.mkv";

        static async Task<bool> WriteAsync(DbContextOptions<ApplicationContext> options,
            DomainFileMapping mapping)
        {
            await using var context = new ApplicationContext(options);
            var repository = new FileMappingRepository(context, options);
            try
            {
                await repository.AddRangeAsync([mapping], CancellationToken.None);
                return true;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            WriteAsync(_options, Mapping(firstInfo.Id, CollidingPath)),
            WriteAsync(_options, Mapping(secondInfo.Id, CollidingPath)));

        Assert.AreEqual(1, results.Count(result => result));
        await using var verification = new ApplicationContext(_options);
        Assert.AreEqual(1, await verification.FileMappings.CountAsync());
        var versions = await verification.AnimationInfo.OrderBy(info => info.Id)
            .Select(info => info.StateVersion).ToListAsync();
        CollectionAssert.AreEquivalent(new long[] { 0, 1 }, versions);
    }

    private static async Task<AnimationInfoEntity> SeedDownloadedAnimationAsync()
    {
        await using var context = new ApplicationContext(_options);
        var info = new AnimationInfoEntity
        {
            Id = Guid.NewGuid(),
            Title = "integration test",
            IsDownloadFinished = true,
            FileStore = "local",
            StorePath = "/store/" + Guid.NewGuid().ToString("N")
        };
        context.AnimationInfo.Add(info);
        await context.SaveChangesAsync();
        return info;
    }

    private static DomainFileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/" + Guid.NewGuid(), "local");
}
