namespace SecondDimensionWatcherReDive.Models;

public class BangumiSubgroup
{
    public Guid Id { get; set; }

    public Guid SeasonBangumiId { get; set; }
    public SeasonBangumi SeasonBangumi { get; set; } = null!;

    /// <summary>Mikanani subgroup ID (e.g., 370)</summary>
    public int MikanSubgroupId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset ScrapedAt { get; set; }
}
