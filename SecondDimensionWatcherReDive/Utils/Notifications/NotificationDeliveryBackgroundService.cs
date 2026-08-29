using System.Net;
using System.Text;
using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.Notifications;

public sealed partial class NotificationDeliveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<NotificationDeliveryBackgroundService> logger) : BackgroundService
{
    private const int MaxAttempts = 8;
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
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
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>();
        var now = DateTimeOffset.UtcNow;
        var messages = await repository.ClaimDueAsync(
            now, now + LeaseDuration, BatchSize, cancellationToken);
        foreach (var message in messages)
            await DeliverAsync(repository, message, cancellationToken);
        return messages.Count;
    }

    private async Task DeliverAsync(
        INotificationOutboxRepository repository,
        NotificationOutboxMessage message,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (IsQuietHours(now))
        {
            await repository.RescheduleAsync(message.Id, now.AddMinutes(15), cancellationToken);
            return;
        }

        var endpoint = configuration["Notifications:Webhook:Url"];
        if (!configuration.GetValue<bool>("Notifications:Webhook:Enabled")
            || string.IsNullOrWhiteSpace(endpoint))
        {
            await repository.RescheduleAsync(message.Id, now.AddMinutes(5), cancellationToken);
            return;
        }

        var attempt = message.AttemptCount + 1;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("X-SDW-Event-Id", message.Id.ToString("D"));
            request.Content = new StringContent(CreatePayload(message), Encoding.UTF8, "application/json");
            using var response = await httpClientFactory.CreateClient("NotificationWebhook")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await repository.MarkDeliveredAsync(message.Id, now, cancellationToken);
                LogDelivered(logger, message.Id, message.Type);
                return;
            }

            var retry = IsRetryable(response.StatusCode) && attempt < MaxAttempts;
            await repository.MarkFailedAsync(
                message.Id,
                attempt,
                now,
                retry ? now + RetryDelay(attempt) : null,
                $"HTTP {(int)response.StatusCode}",
                cancellationToken);
            LogDeliveryRejected(logger, message.Id, message.Type, (int)response.StatusCode, retry);
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
                attempt,
                now,
                retry ? now + RetryDelay(attempt) : null,
                exception.GetType().Name,
                cancellationToken);
            // HttpClient exception text can contain the request URI. The webhook URL may
            // carry an access token, so only log the exception type here.
            LogDeliveryFailed(logger, message.Id, message.Type, exception.GetType().Name, retry);
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
            eventId = message.Id,
            type = char.ToLowerInvariant(message.Type.ToString()[0]) + message.Type.ToString()[1..],
            message.Title,
            message.Body,
            message.DeepLink,
            message.OccurredAt,
            payload
        }, JsonOptions);
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
        Message = "Delivered notification {NotificationId} ({NotificationType})")]
    private static partial void LogDelivered(
        ILogger logger,
        Guid notificationId,
        Framework.Notifications.NotificationEventType notificationType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Webhook rejected notification {NotificationId} ({NotificationType}) with HTTP {StatusCode}; retry={Retry}")]
    private static partial void LogDeliveryRejected(
        ILogger logger,
        Guid notificationId,
        Framework.Notifications.NotificationEventType notificationType,
        int statusCode,
        bool retry);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to deliver notification {NotificationId} ({NotificationType}) with {ErrorType}; retry={Retry}")]
    private static partial void LogDeliveryFailed(
        ILogger logger,
        Guid notificationId,
        Framework.Notifications.NotificationEventType notificationType,
        string errorType,
        bool retry);
}
