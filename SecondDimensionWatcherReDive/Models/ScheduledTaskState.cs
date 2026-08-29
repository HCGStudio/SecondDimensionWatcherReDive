namespace SecondDimensionWatcherReDive.Models;

public sealed class ScheduledTaskState
{
    public string TaskId { get; set; } = string.Empty;
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public long RunCount { get; set; }
    public string? LastError { get; set; }
}
