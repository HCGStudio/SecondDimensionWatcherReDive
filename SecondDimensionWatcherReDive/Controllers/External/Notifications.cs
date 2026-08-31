using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record NotificationDeliveryItem(
    Guid Id,
    Guid EventId,
    string Channel,
    string Type,
    string Status,
    int AttemptCount,
    DateTimeOffset OccurredAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? LastError);

internal sealed record TestNotificationResponse(Guid EventId);

internal sealed record WebPushConfigurationResponse(
    bool Enabled,
    string VapidPublicKey);

internal sealed record WebPushSubscriptionKeysRequest(
    [Required] string? P256dh,
    [Required] string? Auth);

internal sealed record RegisterWebPushSubscriptionRequest(
    [Required] string? Endpoint,
    [Required] WebPushSubscriptionKeysRequest? Keys);

internal sealed record RemoveWebPushSubscriptionRequest(
    [Required] string? Endpoint);

internal sealed record WebPushSubscriptionSummary(
    Guid Id,
    string EndpointOrigin,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError);
