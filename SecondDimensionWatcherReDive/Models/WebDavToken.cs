namespace SecondDimensionWatcherReDive.Models;

public class WebDavToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public UserAccount User { get; set; } = null!;

    public string Username { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string Scope { get; set; } = "read";

    public string VirtualRoot { get; set; } = "/";

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
