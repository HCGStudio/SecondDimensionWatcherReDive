namespace SecondDimensionWatcherReDive.Models;

public sealed class AnimationCatalogEntry
{
    public Guid AnimationId { get; set; }
    public Animation Animation { get; set; } = null!;
    public string TmdbId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public int EpisodeCount { get; set; }
    public int ReleaseCount { get; set; }
    public int AutomationAttentionCount { get; set; }
    public DateTimeOffset LatestPublishTime { get; set; }
}
