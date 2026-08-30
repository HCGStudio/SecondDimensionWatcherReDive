using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Owns PostgreSQL setup and seed data for todo/incident repository integration
/// tests without exposing the EF context outside the repository boundary.
/// </summary>
internal sealed class TodoRepositoryPostgreSqlTestFixture(string connectionString)
{
    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions =
        new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString)
            .Options;

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"TodoItemStates\", \"Incidents\", \"AnimationInfo\" RESTART IDENTITY CASCADE",
            cancellationToken);
    }

    public async Task<Guid> SeedAnimationInfoAsync(
        string title,
        DateTimeOffset publishTime,
        SubscriptionAutomationDisposition? automationDisposition,
        MetadataReviewStatus metadataStatus,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var entity = new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = title,
            PublishTime = publishTime,
            AutomationDisposition = automationDisposition,
            MetadataStatus = metadataStatus
        };
        await context.AnimationInfo.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<Incident> UpsertIncidentAsync(
        Incident incident,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new IncidentRepository(context).UpsertAsync(incident, cancellationToken);
    }

    public async Task<Incident?> ResolveIncidentAsync(
        string fingerprint,
        DateTimeOffset resolvedAt,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new IncidentRepository(context).ResolveByFingerprintAsync(
            fingerprint,
            resolvedAt,
            cancellationToken);
    }

    public async Task<TodoPage> GetTodosAsync(
        bool includeRead,
        bool includeSnoozed,
        DateTimeOffset now,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new TodoRepository(context).GetAsync(
            includeRead,
            includeSnoozed,
            now,
            skip,
            take,
            cancellationToken);
    }

    public async Task SetTodoStateAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset? readAt,
        bool updateReadAt,
        DateTimeOffset? snoozedUntil,
        bool updateSnoozedUntil,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await new TodoRepository(context).SetStateAsync(
            keys,
            readAt,
            updateReadAt,
            snoozedUntil,
            updateSnoozedUntil,
            cancellationToken);
    }

    public async Task<int> GetTodoStateCountAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.TodoItemStates.CountAsync(cancellationToken);
    }
}
