namespace SecondDimensionWatcherReDive.Models;

public class PlaybackProgress
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AnimationInfoId { get; set; }

    public AnimationInfo? AnimationInfo { get; set; }

    public string VirtualPath { get; set; } = string.Empty;

    public double PositionSeconds { get; set; }

    public double DurationSeconds { get; set; }

    public bool IsWatched { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? WatchedAt { get; set; }
}
