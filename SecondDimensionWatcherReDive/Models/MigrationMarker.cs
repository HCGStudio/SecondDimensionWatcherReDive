namespace SecondDimensionWatcherReDive.Models;

public class MigrationMarker
{
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset AppliedAt { get; set; }
}
