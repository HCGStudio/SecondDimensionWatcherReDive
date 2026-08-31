using System.Net;
using System.Text;
using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.Http;
using WebPush;

namespace SecondDimensionWatcherReDive.Utils.Notifications;

public sealed partial class NotificationDeliveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<NotificationDeliveryBackgroundService> logger) : BackgroundService
{
    private const int MaxAttempts = 8;
    private const int BatchSize = 20;
    // Keep the serialized plaintext comfortably below the Web Push record limit;
    // encryption metadata and padding also consume bytes in the 4096-byte record.
    private const int MaxWebPushPayloadBytes = 3000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DeliverBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogBatchFailed(logger, exception);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    internal async Task<int> DeliverBatchAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationOutboxMessage> messages;
        await using (var claimScope = scopeFactory.CreateAsyncScope())
        {
            var repository = claimScope.ServiceProvider
                .GetRequiredService<INotificationOutboxRepository>();
            messages = await repository.ClaimDueAsync(
                LeaseDuration, BatchSize, cancellationToken);
        }

        // Start every claimed delivery immediately. The named HttpClient bounds
        // sockets and the per-request deadline includes handler queueing, so the
        // complete batch finishes inside its lease. Each task needs its own scoped
        // repository because DbContext is not safe for concurrent use.
        await Task.WhenAll(messages.Select(message =>
            DeliverClaimedAsync(message, cancellationToken)));
        return messages.Count;
    }

    private async Task DeliverClaimedAsync(
        NotificationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var subscriptions = scope.ServiceProvider
            .GetRequiredService<IWebPushSubscriptionRepository>();
        await DeliverAsync(repository, subscriptions, message, cancellationToken);
    }

    private async Task DeliverAsync(
        INotificationOutboxRepository repository,
        IWebPushSubscriptionRepository subscriptions,
        NotificationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (message.Type != Framework.Notifications.NotificationEventType.Test
            && IsQuietHours(now))
        {
            await repository.RescheduleAsync(
                message.Id, message.NextAttemptAt, now.AddMinutes(15), cancellationToken);
            return;
        }

        switch (message.Channel)
        {
            case NotificationChannel.Webhook:
                await DeliverWebhookAsync(repository, message, now, cancellationToken);
                break;
            case NotificationChannel.WebPush:
                await DeliverWebPushAsync(
                    repository,
                    subscriptions,
                    message,
                    now,
                    cancellationToken);
                break;
            default:
                await repository.MarkFailedAsync(
                    message.Id,
                    message.NextAttemptAt,
                    message.AttemptCount + 1,
                    now,
                    null,
                    "UnsupportedChannel",
                    cancellationToken);
                break;
        }
    }

    private async Task DeliverWebhookAsync(
        INotificationOutboxRepository repository,
        NotificationOutboxMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {

        var endpoint = configuration["Notifications:Webhook:Url"];
        if (!configuration.GetValue<bool>("Notifications:Webhook:Enabled")
            || string.IsNullOrWhiteSpace(endpoint))
        {
            await repository.RescheduleAsync(
                message.Id, message.NextAttemptAt, now.AddMinutes(5), cancellationToken);
            return;
        }

        var attempt = message.AttemptCount + 1;
        try
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
                throw new OutboundRequestBlockedException("The webhook URL is invalid.");
            OutboundAddressPolicy.ValidateUriShape(endpointUri);
            if (endpointUri.Scheme == Uri.UriSchemeHttp && !endpointUri.IsLoopback)
                throw new OutboundRequestBlockedException(
                    "Plain HTTP is allowed only for loopback webhook endpoints.");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri);
            request.Headers.TryAddWithoutValidation(
                "X-SDW-Event-Id",
                message.EventId.ToString("D"));
            request.Content = new StringContent(CreatePayload(message), Encoding.UTF8, "application/json");
            using var response = await httpClientFactory.CreateClient("NotificationWebhook")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await repository.MarkDeliveredAsync(
                    message.Id, message.NextAttemptAt, now, cancellationToken);
                LogDelivered(logger, message.EventId, message.Channel, message.Type);
                return;
            }

            var retry = IsRetryable(response.StatusCode) && attempt < MaxAttempts;
            await repository.MarkFailedAsync(
                message.Id,
                message.NextAttemptAt,
                attempt,
                now,
                retry ? now + RetryDelay(attempt) : null,
                $"HTTP {(int)response.StatusCode}",
                cancellationToken);
            LogDeliveryRejected(
                logger,
                message.EventId,
                message.Channel,
                message.Type,
                (int)response.StatusCode,
                retry);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var retry = attempt < MaxAttempts;
            await repository.MarkFailedAsync(
                message.Id,
                message.NextAttemptAt,
                attempt,
                now,
                retry ? now + RetryDelay(attempt) : null,
                exception.GetType().Name,
                cancellationToken);
            // HttpClient exception text can contain the request URI. The webhook URL may
            // carry an access token, so only log the exception type here.
            LogDeliveryFailed(
                logger,
                message.EventId,
                message.Channel,
                message.Type,
                exception.GetType().Name,
                retry);
        }
    }

    private async Task DeliverWebPushAsync(
        INotificationOutboxRepository repository,
        IWebPushSubscriptionRepository subscriptions,
        NotificationOutboxMessage message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Notifications:WebPush:Enabled"))
        {
            await repository.RescheduleAsync(
                message.Id, message.NextAttemptAt, now.AddMinutes(5), cancellationToken);
            return;
        }

        var attempt = message.AttemptCount + 1;
        try
        {
            if (!message.WebPushSubscriptionId.HasValue)
                throw new InvalidDataException("The Web Push target is missing.");
            var subscription = await subscriptions.FindByIdAsync(
                message.WebPushSubscriptionId.Value,
                cancellationToken);
            if (subscription is null)
            {
                await repository.MarkFailedAsync(
                    message.Id,
                    message.NextAttemptAt,
                    attempt,
                    now,
                    null,
                    "SubscriptionRemoved",
                    cancellationToken);
                return;
            }

            var subject = configuration["Notifications:WebPush:Subject"];
            var publicKey = configuration["Notifications:WebPush:VapidPublicKey"];
            var privateKey = configuration["Notifications:WebPush:VapidPrivateKey"];
            if (string.IsNullOrWhiteSpace(subject)
                || string.IsNullOrWhiteSpace(publicKey)
                || string.IsNullOrWhiteSpace(privateKey))
            {
                await repository.RescheduleAsync(
                    message.Id,
                    message.NextAttemptAt,
                    now.AddMinutes(5),
                    cancellationToken);
                return;
            }

            using var client = new WebPushClient(httpClientFactory.CreateClient("WebPush"));
            await client.SendNotificationAsync(
                new PushSubscription(subscription.Endpoint, subscription.P256Dh, subscription.Auth),
                CreateWebPushPayload(message),
                new VapidDetails(subject, publicKey, privateKey),
                cancellationToken);
            if (await repository.MarkDeliveredAsync(
                    message.Id,
                    message.NextAttemptAt,
                    now,
                    cancellationToken))
                await TryRecordSubscriptionSuccessAsync(
                    subscriptions,
                    subscription.Id,
                    now,
                    cancellationToken);
            LogDelivered(logger, message.EventId, message.Channel, message.Type);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WebPushException exception)
        {
            var expired = exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone;
            var retry = !expired && IsRetryable(exception.StatusCode) && attempt < MaxAttempts;
            var error = expired ? "SubscriptionExpired" : $"HTTP {(int)exception.StatusCode}";
            await repository.MarkFailedAsync(
                message.Id,
                message.NextAttemptAt,
                attempt,
                now,
                retry ? now + RetryDelay(attempt) : null,
                error,
                cancellationToken);
            if (message.WebPushSubscriptionId.HasValue)
            {
                if (expired)
                    await TryRemoveSubscriptionAsync(
                        subscriptions,
                        message.WebPushSubscriptionId.Value,
                        cancellationToken);
                else
                    await TryRecordSubscriptionFailureAsync(
                        subscriptions,
                        message.WebPushSubscriptionId.Value,
                        now,
                        error,
                        cancellationToken);
            }
            LogDeliveryRejected(
                logger,
                message.EventId,
                message.Channel,
                message.Type,
                (int)exception.StatusCode,
                retry);
        }
        catch (Exception exception)
        {
            var retry = attempt < MaxAttempts;
            var error = exception.GetType().Name;
            await repository.MarkFailedAsync(
                message.Id,
                message.NextAttemptAt,
                attempt,
                now,
                retry ? now + RetryDelay(attempt) : null,
                error,
                cancellationToken);
            if (message.WebPushSubscriptionId.HasValue)
                await TryRecordSubscriptionFailureAsync(
                    subscriptions,
                    message.WebPushSubscriptionId.Value,
                    now,
                    error,
                    cancellationToken);
            // WebPushException and HttpClient messages can include the capability
            // endpoint. Persist and log only a bounded type/status token.
            LogDeliveryFailed(
                logger,
                message.EventId,
                message.Channel,
                message.Type,
                error,
                retry);
        }
    }

    private async Task TryRecordSubscriptionSuccessAsync(
        IWebPushSubscriptionRepository subscriptions,
        Guid id,
        DateTimeOffset succeededAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await subscriptions.RecordSuccessAsync(id, succeededAt, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSubscriptionStateUpdateFailed(logger, id, exception.GetType().Name);
        }
    }

    private async Task TryRecordSubscriptionFailureAsync(
        IWebPushSubscriptionRepository subscriptions,
        Guid id,
        DateTimeOffset failedAt,
        string error,
        CancellationToken cancellationToken)
    {
        try
        {
            await subscriptions.RecordFailureAsync(id, failedAt, error, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSubscriptionStateUpdateFailed(logger, id, exception.GetType().Name);
        }
    }

    private async Task TryRemoveSubscriptionAsync(
        IWebPushSubscriptionRepository subscriptions,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            await subscriptions.RemoveAsync(id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSubscriptionStateUpdateFailed(logger, id, exception.GetType().Name);
        }
    }

    private static string CreatePayload(NotificationOutboxMessage message)
    {
        JsonElement? payload = null;
        if (!string.IsNullOrWhiteSpace(message.PayloadJson))
        {
            using var document = JsonDocument.Parse(message.PayloadJson);
            payload = document.RootElement.Clone();
        }

        return JsonSerializer.Serialize(new
        {
            eventId = message.EventId,
            type = char.ToLowerInvariant(message.Type.ToString()[0]) + message.Type.ToString()[1..],
            message.Title,
            message.Body,
            message.DeepLink,
            message.OccurredAt,
            payload
        }, JsonOptions);
    }

    private static string CreateWebPushPayload(NotificationOutboxMessage message)
    {
        var title = LimitUtf8(message.Title, 256);
        var body = LimitUtf8(message.Body, 1024);
        var deepLink = LimitUtf8(message.DeepLink, 1024);
        var serialized = SerializeWebPushPayload(message, title, body, deepLink);
        if (Encoding.UTF8.GetByteCount(serialized) <= MaxWebPushPayloadBytes)
            return serialized;

        // JSON escaping can expand a single input rune to several bytes. Fit the
        // actual serialized payload, preserving the click target until after the
        // visible text has been reduced.
        body = FitWebPushField(
            body,
            candidate => SerializeWebPushPayload(message, title, candidate, deepLink));
        serialized = SerializeWebPushPayload(message, title, body, deepLink);
        if (Encoding.UTF8.GetByteCount(serialized) <= MaxWebPushPayloadBytes)
            return serialized;

        title = FitWebPushField(
            title,
            candidate => SerializeWebPushPayload(message, candidate, body, deepLink));
        serialized = SerializeWebPushPayload(message, title, body, deepLink);
        if (Encoding.UTF8.GetByteCount(serialized) <= MaxWebPushPayloadBytes)
            return serialized;

        deepLink = FitWebPushField(
            deepLink,
            candidate => SerializeWebPushPayload(message, title, body, candidate));
        return SerializeWebPushPayload(message, title, body, deepLink);
    }

    private static string SerializeWebPushPayload(
        NotificationOutboxMessage message,
        string title,
        string body,
        string deepLink) =>
        JsonSerializer.Serialize(new
        {
            eventId = message.EventId,
            type = char.ToLowerInvariant(message.Type.ToString()[0]) + message.Type.ToString()[1..],
            title,
            body,
            deepLink,
            message.OccurredAt
        }, JsonOptions);

    private static string FitWebPushField(
        string value,
        Func<string, string> serialize)
    {
        var offsets = new List<int> { 0 };
        var offset = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            offset += rune.Utf16SequenceLength;
            offsets.Add(offset);
        }

        var lower = 0;
        var upper = offsets.Count - 1;
        while (lower < upper)
        {
            var candidateLength = lower + (upper - lower + 1) / 2;
            var candidate = value[..offsets[candidateLength]];
            if (Encoding.UTF8.GetByteCount(serialize(candidate)) <= MaxWebPushPayloadBytes)
                lower = candidateLength;
            else
                upper = candidateLength - 1;
        }
        return value[..offsets[lower]];
    }

    private static string LimitUtf8(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            return value;
        var builder = new StringBuilder(value.Length);
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (bytes + runeBytes > maximumBytes)
                break;
            builder.Append(rune.ToString());
            bytes += runeBytes;
        }
        return builder.ToString();
    }

    private bool IsQuietHours(DateTimeOffset now)
    {
        var start = configuration.GetValue<TimeSpan?>("Notifications:QuietHours:Start");
        var end = configuration.GetValue<TimeSpan?>("Notifications:QuietHours:End");
        if (!start.HasValue || !end.HasValue || start == end) return false;

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(
                configuration["Notifications:QuietHours:TimeZone"] ?? "UTC");
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone).TimeOfDay;
        return start < end
            ? local >= start && local < end
            : local >= start || local < end;
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        || (int)statusCode >= 500;

    private static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempt - 1), 6 * 60 * 60));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification delivery batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Delivered notification {NotificationId} via {NotificationChannel} ({NotificationType})")]
    private static partial void LogDelivered(
        ILogger logger,
        Guid notificationId,
        NotificationChannel notificationChannel,
        Framework.Notifications.NotificationEventType notificationType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Notification channel {NotificationChannel} rejected {NotificationId} ({NotificationType}) with HTTP {StatusCode}; retry={Retry}")]
    private static partial void LogDeliveryRejected(
        ILogger logger,
        Guid notificationId,
        NotificationChannel notificationChannel,
        Framework.Notifications.NotificationEventType notificationType,
        int statusCode,
        bool retry);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to deliver notification {NotificationId} via {NotificationChannel} ({NotificationType}) with {ErrorType}; retry={Retry}")]
    private static partial void LogDeliveryFailed(
        ILogger logger,
        Guid notificationId,
        NotificationChannel notificationChannel,
        Framework.Notifications.NotificationEventType notificationType,
        string errorType,
        bool retry);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to update Web Push subscription {SubscriptionId} state with {ErrorType}")]
    private static partial void LogSubscriptionStateUpdateFailed(
        ILogger logger,
        Guid subscriptionId,
        string errorType);
}
