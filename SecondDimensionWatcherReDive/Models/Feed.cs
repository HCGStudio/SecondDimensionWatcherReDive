namespace SecondDimensionWatcherReDive.Models;

public class Feed
{
    public Guid Id { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
