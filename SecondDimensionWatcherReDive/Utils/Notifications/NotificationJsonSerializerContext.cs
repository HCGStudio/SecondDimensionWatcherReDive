using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Utils.Notifications;

internal sealed record NotificationWebhookPayload(
    Guid EventId,
    string Type,
    string Title,
    string Body,
    string DeepLink,
    DateTimeOffset OccurredAt,
    JsonElement? Payload);

internal sealed record NotificationWebPushPayload(
    Guid EventId,
    string Type,
    string Title,
    string Body,
    string DeepLink,
    DateTimeOffset OccurredAt);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(NotificationWebhookPayload))]
[JsonSerializable(typeof(NotificationWebPushPayload))]
[JsonSerializable(typeof(JsonElement))]
internal partial class NotificationJsonSerializerContext : JsonSerializerContext;
