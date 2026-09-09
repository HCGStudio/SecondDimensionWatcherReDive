using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;

namespace SecondDimensionWatcherReDive.Models;

public sealed class NotificationOutboxMessage
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string DeduplicationKey { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public Guid? WebPushSubscriptionId { get; set; }
    public string? PluginProviderId { get; set; }
    public string? PluginPublisherIdentity { get; set; }
    public NotificationEventType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DeepLink { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public string? LastError { get; set; }
}

internal sealed class PluginNotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxMessage> builder)
    {
        builder.Property(message => message.PluginProviderId).HasMaxLength(256);
        builder.Property(message => message.PluginPublisherIdentity).HasMaxLength(128);
    }
}
