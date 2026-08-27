namespace SecondDimensionWatcherReDive.Models;

public class FileNameRegexRule
{
    public Guid Id { get; set; }
    public Guid AnimationId { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
