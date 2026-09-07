namespace SecondDimensionWatcherReDive.Models;

public class PlaybackPreference
{
    public Guid UserId { get; set; }

    public UserProfile? Profile { get; set; }

    public string? SubtitleLanguage { get; set; }

    public string? SubtitleTrackLabel { get; set; }

    public string? AudioLanguage { get; set; }

    public string? AudioTrackLabel { get; set; }

    public bool AutoPlayNext { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
