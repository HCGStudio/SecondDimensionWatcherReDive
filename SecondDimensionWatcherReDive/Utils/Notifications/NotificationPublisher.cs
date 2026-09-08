using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.PluginPlatform;

namespace SecondDimensionWatcherReDive.Utils.Notifications;

public sealed partial class NotificationPublisher(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<NotificationPublisher> logger,
    IPluginProviderRegistry pluginProviders) : INotificationPublisher
{
    private const int MaxDeduplicationKeyLength = 256;
    private const int MaxPayloadBytes = 64 * 1024;

    public async Task<bool> PublishAsync(NotificationEvent notificationEvent, CancellationToken cancellationToken)
        => (await PublishCoreAsync(notificationEvent, cancellationToken)).NewlyInserted;

    public async Task<bool> EnsurePublishedAsync(NotificationEvent notificationEvent, CancellationToken cancellationToken)
    {
        var result = await PublishCoreAsync(notificationEvent, cancellationToken);
        return result.HasTargets && result.AllPersisted;
    }

    public async Task<NotificationPublicationOutcome> PublishDurablyAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken)
    {
        var result = await PublishCoreAsync(notificationEvent, cancellationToken);
        return !result.HasTargets
            ? NotificationPublicationOutcome.NotRequired
            : result.AllPersisted
                ? NotificationPublicationOutcome.Persisted
                : NotificationPublicationOutcome.Failed;
    }

    private async Task<PublicationResult> PublishCoreAsync(
        NotificationEvent notificationEvent,
        CancellationToken cancellationToken)
    {
        if (notificationEvent.Type != NotificationEventType.Test
            && !SubscribedEvents(configuration["Notifications:Events"]).Contains(notificationEvent.Type))
            return new(false, false, true);

        try
        {
            var webhookEnabled = configuration.GetValue<bool>("Notifications:Webhook:Enabled");
            var webPushEnabled = configuration.GetValue<bool>("Notifications:WebPush:Enabled");
            // A circuit breaker suspends delivery, not publication. Keep notifications durable
            // while an enabled provider is recovering; disabled providers receive no new events.
            var pluginTargets = pluginProviders.GetNotificationTargets()
                .Where(target => target.AcceptsNotifications).ToArray();
            if (!webhookEnabled && !webPushEnabled && pluginTargets.Length == 0)
                return new(false, false, true);

            var fallbackEventId = notificationEvent.Id ?? Guid.NewGuid();
            var occurredAt = notificationEvent.OccurredAt ?? DateTimeOffset.UtcNow;
            var title = Limit(notificationEvent.Title, 256);
            var body = Limit(notificationEvent.Body, 2048);
            var deepLink = Limit(notificationEvent.DeepLink, 2048);
            var payload = NormalizePayload(notificationEvent.PayloadJson);
            var baseDeduplicationKey = NormalizeDeduplicationKey(
                notificationEvent.DeduplicationKey, notificationEvent.Type, fallbackEventId);
            var eventId = notificationEvent.Id
                          ?? DeriveEventId(notificationEvent.Type, baseDeduplicationKey);
            var hasTargets = false;
            var newlyInserted = false;
            var allPersisted = true;

            async Task PersistTargetAsync(
                NotificationChannel channel,
                Guid? subscriptionId = null,
                PluginNotificationTarget? pluginTarget = null)
            {
                hasTargets = true;
                var key = pluginTarget is null
                    ? NormalizeTargetDeduplicationKey(baseDeduplicationKey, channel, subscriptionId)
                    : BoundDeduplicationKey(
                        $"{pluginTarget.Id}:{pluginTarget.PublisherIdentity}:{baseDeduplicationKey}");
                var message = new NotificationOutboxMessage(
                    Guid.NewGuid(), eventId, key, channel, subscriptionId, notificationEvent.Type,
                    title, body, deepLink, payload, occurredAt, NotificationDeliveryStatus.Pending,
                    0, occurredAt, null, null, null)
                {
                    PluginProviderId = pluginTarget?.Id,
                    PluginPublisherIdentity = pluginTarget?.PublisherIdentity
                };
                try
                {
                    // Each target owns its scope: failure tracking from one insert cannot poison
                    // another target, and a partial fan-out can be retried with the same keys.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
                    var inserted = await outbox.EnqueueAsync(message, cancellationToken);
                    newlyInserted |= inserted;
                    allPersisted &= inserted || await outbox.ContainsDeduplicationKeyAsync(key, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    allPersisted = false;
                    LogEnqueueFailed(logger, exception, notificationEvent.Type);
                }
            }

            if (webhookEnabled)
                await PersistTargetAsync(NotificationChannel.Webhook);
            foreach (var target in pluginTargets)
                await PersistTargetAsync(NotificationChannel.Plugin, pluginTarget: target);
            if (webPushEnabled)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var subscriptions = await scope.ServiceProvider
                        .GetRequiredService<IWebPushSubscriptionRepository>().GetAllAsync(cancellationToken);
                    foreach (var subscription in subscriptions)
                        await PersistTargetAsync(NotificationChannel.WebPush, subscription.Id);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Failed target discovery is not evidence that no destination exists.
                    hasTargets = true;
                    allPersisted = false;
                    LogEnqueueFailed(logger, exception, notificationEvent.Type);
                }
            }
            return new(hasTargets, newlyInserted, allPersisted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogEnqueueFailed(logger, exception, notificationEvent.Type);
            return new(true, false, false);
        }
    }

    private sealed record PublicationResult(bool HasTargets, bool NewlyInserted, bool AllPersisted);

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to persist notification {NotificationType}")]
    private static partial void LogEnqueueFailed(
        ILogger logger,
        Exception exception,
        NotificationEventType notificationType);
}
