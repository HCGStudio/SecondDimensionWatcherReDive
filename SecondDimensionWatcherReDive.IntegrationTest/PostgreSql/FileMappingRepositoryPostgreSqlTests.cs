using Microsoft.EntityFrameworkCore;
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
    private static TodoRepositoryPostgreSqlTestFixture TodoFixture = null!;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        await Database.StartAsync();
        Fixture = new FileMappingRepositoryPostgreSqlTestFixture(Database.GetConnectionString());
        await Fixture.InitializeAsync(CancellationToken.None);
        TodoFixture = new TodoRepositoryPostgreSqlTestFixture(Database.GetConnectionString());
    }

    [ClassCleanup]
    public static async Task CleanupAsync() => await Database.DisposeAsync();

    [TestInitialize]
    public async Task ResetDatabaseAsync()
    {
        await Fixture.ResetAsync(CancellationToken.None);
        await TodoFixture.ResetAsync(CancellationToken.None);
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
    public async Task RecurringIncident_GetsFreshTodoState_WhileFirstOccurrenceKeepsLegacyKey()
    {
        var now = DateTimeOffset.UtcNow;
        var incidentId = Guid.NewGuid();
        var first = await TodoFixture.UpsertIncidentAsync(
            Incident(incidentId, now),
            CancellationToken.None);

        Assert.AreEqual(1, first.Occurrence);
        var firstKey = $"incident:{incidentId}";
        var initialTodos = await TodoFixture.GetTodosAsync(
            false, false, now, 0, 10, CancellationToken.None);
        Assert.HasCount(1, initialTodos.Items);
        Assert.AreEqual(firstKey, initialTodos.Items[0].Key);

        await TodoFixture.SetTodoStateAsync(
            [firstKey],
            now,
            true,
            now.AddHours(1),
            true,
            CancellationToken.None);
        var hidden = await TodoFixture.GetTodosAsync(
            false, false, now, 0, 10, CancellationToken.None);
        Assert.AreEqual(0, hidden.TotalCount);
        Assert.AreEqual(0, hidden.UnreadCount);

        await TodoFixture.ResolveIncidentAsync(
            first.Fingerprint,
            now.AddMinutes(1),
            CancellationToken.None);
        var reopened = await TodoFixture.UpsertIncidentAsync(
            Incident(Guid.NewGuid(), now.AddMinutes(2)),
            CancellationToken.None);
        var duplicateReport = await TodoFixture.UpsertIncidentAsync(
            Incident(Guid.NewGuid(), now.AddMinutes(3)),
            CancellationToken.None);

        Assert.AreEqual(incidentId, reopened.Id);
        Assert.AreEqual(2, reopened.Occurrence);
        Assert.AreEqual(2, duplicateReport.Occurrence);
        var currentTodos = await TodoFixture.GetTodosAsync(
            false, false, now.AddMinutes(3), 0, 10, CancellationToken.None);
        Assert.HasCount(1, currentTodos.Items);
        Assert.AreEqual($"incident:{incidentId}:2", currentTodos.Items[0].Key);
        Assert.IsNull(currentTodos.Items[0].ReadAt);
        Assert.IsNull(currentTodos.Items[0].SnoozedUntil);
        Assert.AreEqual(1, currentTodos.UnreadCount);
        Assert.AreEqual(1, await TodoFixture.GetTodoStateCountAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task TodoQuery_FiltersCountsAndPaginatesAcrossSourcesInPostgreSql()
    {
        var now = DateTimeOffset.UtcNow;
        var automationIds = new List<Guid>();
        for (var index = 0; index < 4; index++)
        {
            automationIds.Add(await TodoFixture.SeedAnimationInfoAsync(
                $"automation-{index}",
                now.AddMinutes(-index),
                SubscriptionAutomationDisposition.Notified,
                MetadataReviewStatus.Identified,
                CancellationToken.None));
        }
        var metadataId = await TodoFixture.SeedAnimationInfoAsync(
            "metadata",
            now.AddMinutes(-5),
            null,
            MetadataReviewStatus.LowConfidence,
            CancellationToken.None);
        var incident = await TodoFixture.UpsertIncidentAsync(
            Incident(Guid.NewGuid(), now.AddMinutes(-6)),
            CancellationToken.None);

        await TodoFixture.SetTodoStateAsync(
            [$"automation:{automationIds[0]}"],
            now,
            true,
            null,
            false,
            CancellationToken.None);
        await TodoFixture.SetTodoStateAsync(
            [$"metadata:{metadataId}"],
            null,
            false,
            now.AddHours(1),
            true,
            CancellationToken.None);

        var firstPage = await TodoFixture.GetTodosAsync(
            false, false, now, 0, 2, CancellationToken.None);
        var secondPage = await TodoFixture.GetTodosAsync(
            false, false, now, 2, 2, CancellationToken.None);
        var allStates = await TodoFixture.GetTodosAsync(
            true, true, now, 0, 10, CancellationToken.None);

        Assert.AreEqual(4, firstPage.TotalCount);
        Assert.AreEqual(4, firstPage.UnreadCount);
        Assert.HasCount(2, firstPage.Items);
        Assert.HasCount(2, secondPage.Items);
        Assert.AreEqual(6, allStates.TotalCount);
        Assert.AreEqual(4, allStates.UnreadCount);
        Assert.HasCount(6, allStates.Items);
        CollectionAssert.Contains(
            allStates.Items.Select(item => item.Key).ToList(),
            $"incident:{incident.Id}");
    }

    private static FileMapping Mapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/" + Guid.NewGuid(), "local");

    private static Incident Incident(Guid id, DateTimeOffset occurredAt) => new(
        id,
        "feedfailure:integration-test",
        IncidentType.FeedFailure,
        IncidentSeverity.Error,
        "Feed failed",
        "The feed could not be loaded.",
        "https://example.test/feed",
        occurredAt,
        occurredAt,
        null,
        0,
        null,
        null);
}
