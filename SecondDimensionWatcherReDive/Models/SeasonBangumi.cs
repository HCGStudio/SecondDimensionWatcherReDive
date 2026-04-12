namespace SecondDimensionWatcherReDive.Models;

public class SeasonBangumi
{
    public Guid Id { get; set; }

    /// <summary>Mikanani bangumi ID (e.g., 3899)</summary>
    public int MikanId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>0=Sunday, 1=Monday...6=Saturday, 7=Movie, 8=OVA</summary>
    public int DayOfWeek { get; set; }

    /// <summary>Cover image URL from mikanani (relative path)</summary>
    public string? ImageUrl { get; set; }

    public DateTimeOffset ScrapedAt { get; set; }

    public ICollection<BangumiSubgroup> Subgroups { get; set; } = new List<BangumiSubgroup>();
}
