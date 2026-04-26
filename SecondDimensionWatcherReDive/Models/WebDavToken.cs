namespace SecondDimensionWatcherReDive.Models;

public class WebDavToken
{
    public Guid Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
