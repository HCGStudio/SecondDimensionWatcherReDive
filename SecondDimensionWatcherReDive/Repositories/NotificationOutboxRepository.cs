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
        DateTimeOffset now,
        DateTimeOffset leaseUntil,
        int take,
        CancellationToken cancellationToken)
    {
        var candidateIds = await context.NotificationOutboxMessages
            .AsNoTracking()
            .Where(message =>
                (message.Status == NotificationDeliveryStatus.Pending
                 || message.Status == NotificationDeliveryStatus.Processing)
                && message.NextAttemptAt <= now)
            .OrderBy(message => message.NextAttemptAt)
            .ThenBy(message => message.OccurredAt)
            .Select(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var claimedIds = new List<Guid>(candidateIds.Count);
        foreach (var id in candidateIds)
        {
            var affected = await context.NotificationOutboxMessages
                .Where(message => message.Id == id
                                  && (message.Status == NotificationDeliveryStatus.Pending
                                      || message.Status == NotificationDeliveryStatus.Processing)
                                  && message.NextAttemptAt <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.Status, NotificationDeliveryStatus.Processing)
                    .SetProperty(message => message.NextAttemptAt, leaseUntil), cancellationToken);
            if (affected == 1) claimedIds.Add(id);
        }

        if (claimedIds.Count == 0) return [];
        return (await context.NotificationOutboxMessages
                .AsNoTracking()
                .Where(message => claimedIds.Contains(message.Id))
                .OrderBy(message => message.OccurredAt)
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();
    }

    public Task MarkDeliveredAsync(
        Guid id,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken) =>
        context.NotificationOutboxMessages
            .Where(message => message.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, NotificationDeliveryStatus.Delivered)
                .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                .SetProperty(message => message.LastAttemptAt, deliveredAt)
                .SetProperty(message => message.DeliveredAt, deliveredAt)
                .SetProperty(message => message.LastError, (string?)null), cancellationToken);

    public Task MarkFailedAsync(
        Guid id,
        int attemptCount,
        DateTimeOffset attemptedAt,
        DateTimeOffset? nextAttemptAt,
        string error,
        CancellationToken cancellationToken) =>
        context.NotificationOutboxMessages
            .Where(message => message.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status,
                    nextAttemptAt.HasValue
                        ? NotificationDeliveryStatus.Pending
                        : NotificationDeliveryStatus.Failed)
                .SetProperty(message => message.AttemptCount, attemptCount)
                .SetProperty(message => message.LastAttemptAt, attemptedAt)
                .SetProperty(message => message.NextAttemptAt, nextAttemptAt ?? attemptedAt)
                .SetProperty(message => message.LastError, error), cancellationToken);

    public Task RescheduleAsync(
        Guid id,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken) =>
        context.NotificationOutboxMessages
            .Where(message => message.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, NotificationDeliveryStatus.Pending)
                .SetProperty(message => message.NextAttemptAt, nextAttemptAt), cancellationToken);

    public async Task<IReadOnlyList<NotificationOutboxMessage>> GetRecentAsync(
        int take,
        CancellationToken cancellationToken) =>
        (await context.NotificationOutboxMessages
                .AsNoTracking()
                .OrderByDescending(message => message.OccurredAt)
                .Take(take)
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();

    private static NotificationOutboxMessage ToRecord(OutboxEntity message) => new(
        message.Id,
        message.DeduplicationKey,
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
        DeduplicationKey = message.DeduplicationKey,
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
