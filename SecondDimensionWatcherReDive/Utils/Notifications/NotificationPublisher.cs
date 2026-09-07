using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Utils.Notifications;

public sealed partial class NotificationPublisher(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    private const int MaxDeduplicationKeyLength = 256;
    private const int MaxPayloadBytes = 64 * 1024;

    public async Task<bool> PublishAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken)
    {
        var webhookEnabled = configuration.GetValue<bool>("Notifications:Webhook:Enabled");
        var webPushEnabled = configuration.GetValue<bool>("Notifications:WebPush:Enabled");
        if (!webhookEnabled && !webPushEnabled)
            return false;
        if (notificationEvent.Type != NotificationEventType.Test
            && !SubscribedEvents(configuration["Notifications:Events"])
                .Contains(notificationEvent.Type))
            return false;

        try
        {
            var fallbackEventId = notificationEvent.Id ?? Guid.NewGuid();
            var occurredAt = notificationEvent.OccurredAt ?? DateTimeOffset.UtcNow;
            var title = Limit(notificationEvent.Title, 256);
            var body = Limit(notificationEvent.Body, 2048);
            var deepLink = Limit(notificationEvent.DeepLink, 2048);
            var payload = NormalizePayload(notificationEvent.PayloadJson);
            var baseDeduplicationKey = NormalizeDeduplicationKey(
                notificationEvent.DeduplicationKey,
                notificationEvent.Type,
                fallbackEventId);
            // EventId identifies the logical event across all delivery targets and
            // publication retries. Keep the outbox row Id independent so a target
            // added later can be inserted without colliding with an existing row.
            var eventId = notificationEvent.Id
                          ?? DeriveEventId(notificationEvent.Type, baseDeduplicationKey);

            var enqueued = false;

            if (webhookEnabled)
            {
                enqueued |= await TryEnqueueChannelAsync(
                    async () =>
                    {
                        await using var webhookScope = scopeFactory.CreateAsyncScope();
                        var outbox = webhookScope.ServiceProvider
                            .GetRequiredService<INotificationOutboxRepository>();
                        return await EnqueueTargetAsync(
                            outbox,
                            new NotificationOutboxMessage(
                                Guid.NewGuid(),
                                eventId,
                                NormalizeTargetDeduplicationKey(
                                    baseDeduplicationKey,
                                    NotificationChannel.Webhook,
                                    null),
                                NotificationChannel.Webhook,
                                null,
                                notificationEvent.Type,
                                title,
                                body,
                                deepLink,
                                payload,
                                occurredAt,
                                NotificationDeliveryStatus.Pending,
                                0,
                                occurredAt,
                                null,
                                null,
                                null),
                            cancellationToken);
                    },
                    notificationEvent.Type,
                    cancellationToken);
            }

            if (webPushEnabled)
            {
                enqueued |= await TryEnqueueChannelAsync(
                    async () =>
                    {
                        await using var webPushScope = scopeFactory.CreateAsyncScope();
                        var outbox = webPushScope.ServiceProvider
                            .GetRequiredService<INotificationOutboxRepository>();
                        var subscriptionRepository = webPushScope.ServiceProvider
                            .GetRequiredService<IWebPushSubscriptionRepository>();
                        var subscriptions = await subscriptionRepository
                            .GetAllAsync(cancellationToken);
                        var any = false;
                        foreach (var subscription in subscriptions)
                        {
                            var targetDeduplicationKey = NormalizeTargetDeduplicationKey(
                                baseDeduplicationKey,
                                NotificationChannel.WebPush,
                                subscription.Id);
                            any |= await EnqueueTargetAsync(
                                outbox,
                                new NotificationOutboxMessage(
                                    Guid.NewGuid(),
                                    eventId,
                                    targetDeduplicationKey,
                                    NotificationChannel.WebPush,
                                    subscription.Id,
                                    notificationEvent.Type,
                                    title,
                                    body,
                                    deepLink,
                                    payload,
                                    occurredAt,
                                    NotificationDeliveryStatus.Pending,
                                    0,
                                    occurredAt,
                                    null,
                                    null,
                                    null),
                                cancellationToken);
                        }
                        return any;
                    },
                    notificationEvent.Type,
                    cancellationToken);
            }

            if (!enqueued)
                LogDuplicateSkipped(logger, notificationEvent.Type);
            return enqueued;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Notification persistence is deliberately isolated from the core operation.
            LogEnqueueFailed(logger, exception, notificationEvent.Type);
            return false;
        }
    }

    private static async Task<bool> EnqueueTargetAsync(
        INotificationOutboxRepository repository,
        NotificationOutboxMessage message,
        CancellationToken cancellationToken) =>
        await repository.EnqueueAsync(message, cancellationToken);

    private async Task<bool> TryEnqueueChannelAsync(
        Func<Task<bool>> enqueue,
        NotificationEventType notificationType,
        CancellationToken cancellationToken)
    {
        try
        {
            return await enqueue();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogEnqueueFailed(logger, exception, notificationType);
            return false;
        }
    }

    internal static IReadOnlySet<NotificationEventType> SubscribedEvents(string? value)
    {
        var events = new HashSet<NotificationEventType>();
        foreach (var item in (value ?? string.Empty)
                     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse<NotificationEventType>(item, true, out var type))
                events.Add(type);
        }
        return events;
    }

    private static string Limit(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Notification" : value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : TruncateUtf16Safely(normalized, maxLength);
    }

    private static string NormalizeDeduplicationKey(
        string value,
        NotificationEventType type,
        Guid id)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            normalized = $"{type.ToString().ToLowerInvariant()}:{id:D}";
        return BoundDeduplicationKey(normalized);
    }

    private static Guid DeriveEventId(
        NotificationEventType type,
        string baseDeduplicationKey)
    {
        var identity = $"{type}:{baseDeduplicationKey}";
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(identity), digest);
        return new Guid(digest[..16]);
    }

    private static string NormalizeTargetDeduplicationKey(
        string baseDeduplicationKey,
        NotificationChannel channel,
        Guid? subscriptionId)
    {
        var targetPrefix = channel switch
        {
            NotificationChannel.Webhook => "webhook",
            NotificationChannel.WebPush when subscriptionId.HasValue =>
                $"web-push:{subscriptionId.Value:D}",
            _ => throw new ArgumentException("A valid notification target is required.", nameof(channel))
        };
        return BoundDeduplicationKey($"{targetPrefix}:{baseDeduplicationKey}");
    }

    private static string BoundDeduplicationKey(string normalized)
    {
        if (normalized.Length <= MaxDeduplicationKeyLength)
            return normalized;

        var digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
        var prefixLength = MaxDeduplicationKeyLength - digest.Length - 1;
        return $"{TruncateUtf16Safely(normalized, prefixLength)}:{digest}";
    }

    private static string TruncateUtf16Safely(string value, int maximumCodeUnits)
    {
        var length = Math.Min(value.Length, maximumCodeUnits);
        if (length > 0
            && length < value.Length
            && char.IsHighSurrogate(value[length - 1])
            && char.IsLowSurrogate(value[length]))
            length--;
        return value[..length];
    }

    private static string? NormalizePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        if (Encoding.UTF8.GetByteCount(payloadJson) > MaxPayloadBytes)
            throw new InvalidDataException("The notification payload is larger than allowed.");

        using var document = JsonDocument.Parse(payloadJson);
        var normalized = JsonSerializer.Serialize(document.RootElement);
        if (Encoding.UTF8.GetByteCount(normalized) > MaxPayloadBytes)
            throw new InvalidDataException("The normalized notification payload is larger than allowed.");
        return normalized;
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Skipped duplicate notification {NotificationType}")]
    private static partial void LogDuplicateSkipped(
        ILogger logger,
        NotificationEventType notificationType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to enqueue notification {NotificationType}; the core operation remains successful")]
    private static partial void LogEnqueueFailed(
        ILogger logger,
        Exception exception,
        NotificationEventType notificationType);
}
