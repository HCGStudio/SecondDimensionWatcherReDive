namespace SecondDimensionWatcherReDive.Models;

public sealed class UserProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public string? PinHash { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
