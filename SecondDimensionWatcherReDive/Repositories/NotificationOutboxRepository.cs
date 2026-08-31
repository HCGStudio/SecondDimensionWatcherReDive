using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using OutboxEntity = SecondDimensionWatcherReDive.Models.NotificationOutboxMessage;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class NotificationOutboxRepository(Models.ApplicationContext context)
    : INotificationOutboxRepository
{
    public async Task<bool> EnqueueAsync(
        NotificationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        await context.NotificationOutboxMessages.AddAsync(ToEntity(message), cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<IReadOnlyList<NotificationOutboxMessage>> ClaimDueAsync(
        TimeSpan leaseDuration,
        int take,
        CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 100);
        // Claim the ordered batch in one PostgreSQL statement. SKIP LOCKED makes
        // concurrent app instances cooperate without selecting the same work,
        // while RETURNING gives each caller the exact lease value it owns.
        return (await context.NotificationOutboxMessages
                .FromSqlInterpolated($$"""
                    WITH candidates AS (
                        SELECT "Id"
                        FROM "NotificationOutboxMessages"
                        WHERE "Status" IN ('Pending', 'Processing')
                          AND "NextAttemptAt" <= CURRENT_TIMESTAMP
                        ORDER BY "NextAttemptAt", "OccurredAt", "Id"
                        FOR UPDATE SKIP LOCKED
                        LIMIT {{take}}
                    ), claimed AS (
                        UPDATE "NotificationOutboxMessages" AS message
                        SET "Status" = 'Processing',
                            "NextAttemptAt" = CURRENT_TIMESTAMP + {{leaseDuration}}
                        FROM candidates
                        WHERE message."Id" = candidates."Id"
                        RETURNING message.*
                    )
                    SELECT * FROM claimed
                    ORDER BY "OccurredAt", "Id"
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();
    }

    public async Task<bool> MarkDeliveredAsync(
        Guid id,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken) =>
        await context.NotificationOutboxMessages
            .Where(message => message.Id == id
                              && message.Status == NotificationDeliveryStatus.Processing
                              && message.NextAttemptAt == expectedLeaseUntil)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, NotificationDeliveryStatus.Delivered)
                .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                .SetProperty(message => message.LastAttemptAt, deliveredAt)
                .SetProperty(message => message.DeliveredAt, deliveredAt)
                .SetProperty(message => message.LastError, (string?)null), cancellationToken) == 1;

    public async Task<bool> MarkFailedAsync(
        Guid id,
        DateTimeOffset expectedLeaseUntil,
        int attemptCount,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string error,
        CancellationToken cancellationToken) =>
        await context.NotificationOutboxMessages
            .Where(message => message.Id == id
                              && message.Status == NotificationDeliveryStatus.Processing
                              && message.NextAttemptAt == expectedLeaseUntil)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status,
                    nextAttemptAt.HasValue
                        ? NotificationDeliveryStatus.Pending
                        : NotificationDeliveryStatus.Failed)
                .SetProperty(message => message.AttemptCount, attemptCount)
                .SetProperty(message => message.LastAttemptAt, attemptedAt)
                .SetProperty(message => message.NextAttemptAt, nextAttemptAt ?? attemptedAt)
                .SetProperty(message => message.LastError, error), cancellationToken) == 1;

    public async Task<bool> RescheduleAsync(
        Guid id,
        DateTimeOffset expectedLeaseUntil,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken) =>
        await context.NotificationOutboxMessages
            .Where(message => message.Id == id
                              && message.Status == NotificationDeliveryStatus.Processing
                              && message.NextAttemptAt == expectedLeaseUntil)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, NotificationDeliveryStatus.Pending)
                .SetProperty(message => message.NextAttemptAt, nextAttemptAt), cancellationToken) == 1;

    public async Task<IReadOnlyList<NotificationOutboxMessage>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 100);
        return (await context.NotificationOutboxMessages
                .AsNoTracking()
                .OrderByDescending(message => message.OccurredAt)
                .ThenByDescending(message => message.Id)
                .Take(take)
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();
    }

    private static NotificationOutboxMessage ToRecord(OutboxEntity message) => new(
        message.Id,
        message.EventId,
        message.DeduplicationKey,
        message.Channel,
        message.WebPushSubscriptionId,
        message.Type,
        message.Title,
        message.Body,
        message.DeepLink,
        message.PayloadJson,
        message.OccurredAt,
        message.Status,
        message.AttemptCount,
        message.NextAttemptAt,
        message.LastAttemptAt,
        message.DeliveredAt,
        message.LastError);

    private static OutboxEntity ToEntity(NotificationOutboxMessage message) => new()
    {
        Id = message.Id,
        EventId = message.EventId,
        DeduplicationKey = message.DeduplicationKey,
        Channel = message.Channel,
        WebPushSubscriptionId = message.WebPushSubscriptionId,
        Type = message.Type,
        Title = message.Title,
        Body = message.Body,
        DeepLink = message.DeepLink,
        PayloadJson = message.PayloadJson,
        OccurredAt = message.OccurredAt,
        Status = message.Status,
        AttemptCount = message.AttemptCount,
        NextAttemptAt = message.NextAttemptAt,
        LastAttemptAt = message.LastAttemptAt,
        DeliveredAt = message.DeliveredAt,
        LastError = message.LastError
    };
}
