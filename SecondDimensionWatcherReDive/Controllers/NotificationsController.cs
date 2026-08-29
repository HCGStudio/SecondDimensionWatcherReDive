using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class NotificationsController(
    INotificationPublisher publisher,
    INotificationOutboxRepository outboxRepository,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("test")]
    public async Task<ActionResult<TestNotificationResponse>> SendTestAsync(
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Notifications:Webhook:Enabled")
            || string.IsNullOrWhiteSpace(configuration["Notifications:Webhook:Url"]))
            return Conflict(new { message = "Enable and configure the webhook channel first." });

        var id = Guid.NewGuid();
        await publisher.PublishAsync(new NotificationEvent(
            NotificationEventType.Test,
            $"test:{id}",
            "SecondDimensionWatcher Re:Dive test",
            "Your webhook notification channel is configured correctly.",
            "/settings?section=notifications",
            Id: id), cancellationToken);
        return Accepted(new TestNotificationResponse(id));
    }

    [HttpGet("deliveries")]
    public async Task<ActionResult<IReadOnlyList<NotificationDeliveryItem>>> GetDeliveriesAsync(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var items = await outboxRepository.GetRecentAsync(take, cancellationToken);
        return Ok(items.Select(item => new NotificationDeliveryItem(
            item.Id,
            ToJsonName(item.Type),
            item.Status.ToString(),
            item.AttemptCount,
            item.OccurredAt,
            item.LastAttemptAt,
            item.DeliveredAt,
            item.LastError)).ToList());
    }

    private static string ToJsonName(NotificationEventType type)
    {
        var name = type.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
