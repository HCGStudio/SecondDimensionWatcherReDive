namespace SecondDimensionWatcherReDive.Models;

public sealed class TodoItemState
{
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? SnoozedUntil { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
