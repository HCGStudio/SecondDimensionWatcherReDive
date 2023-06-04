namespace SecondDimensionWatcherReDive.Models;

public class AnimationInfo
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset PublishTime { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;

    public string DownloadType { get; set; } = string.Empty;

    public byte[] CachedDownloadData { get; set; } = Array.Empty<byte>();

    public string AdditionalDownloadInfo { get; set; } = string.Empty;

    public bool IsDownloadTracked { get; set; }

    public DateTimeOffset DownloadStartTime { get; set; }

    public DateTimeOffset DownloadEndTime { get; set; }

    public bool IsDownloadFinished { get; set; }

    public string? FileStore { get; set; }

    public string? StorePath { get; set; }

    public AnimationGroup? Group { get; set; }

    public Animation? Animation { get; set; }
}

public class AnimationInfoDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset PublishTime { get; set; }
    public bool IsDownloadTracked { get; set; }
    public bool IsDownloadFinished { get; set; }
    public AnimationGroupDto? Group { get; set; }
    public AnimationDto? Animation { get; set; }
}