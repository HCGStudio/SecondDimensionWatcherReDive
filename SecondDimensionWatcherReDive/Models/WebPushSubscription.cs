namespace SecondDimensionWatcherReDive.Models;

public sealed class WebPushSubscription
{
    public Guid Id { get; set; }
    public string EndpointHash { get; set; } = string.Empty;
    public string ProtectedEndpoint { get; set; } = string.Empty;
    public string ProtectedP256Dh { get; set; } = string.Empty;
    public string ProtectedAuth { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public string? LastError { get; set; }
}
