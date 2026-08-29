namespace SecondDimensionWatcherReDive.Models;

public sealed class LoginSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public Guid ActiveProfileId { get; set; }
    public UserProfile ActiveProfile { get; set; } = null!;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public DateTimeOffset AuthenticatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
