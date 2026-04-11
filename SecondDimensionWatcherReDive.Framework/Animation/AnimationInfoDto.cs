namespace SecondDimensionWatcherReDive.Framework.Animation;

public class AnimationInfoDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset PublishTime { get; set; }
    public bool IsDownloadTracked { get; set; }
    public bool IsDownloadFinished { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public AnimationGroupDto? Group { get; set; }
    public AnimationDto? Animation { get; set; }
}