using SecondDimensionWatcherReDive.Framework.DataRepository;

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

    public Guid? DownloadAttemptId { get; set; }

    public Guid? DownloadCancellationId { get; set; }

    public DateTimeOffset DownloadStartTime { get; set; }

    public DateTimeOffset DownloadEndTime { get; set; }

    public bool IsDownloadFinished { get; set; }

    public string? FileStore { get; set; }

    public string? StorePath { get; set; }

    public int? Season { get; set; }

    public int? Episode { get; set; }

    public AnimationGroup? Group { get; set; }

    public Animation? Animation { get; set; }

    public bool IsAiProcessed { get; set; }

    public int AiRetryCount { get; set; }

    public Guid? SourceFeedId { get; set; }

    public long? ReleaseSizeBytes { get; set; }

    public SubscriptionAutomationDisposition? AutomationDisposition { get; set; }

    public string? AutomationExplanationJson { get; set; }

    public MetadataReviewStatus MetadataStatus { get; set; }

    public double? MetadataConfidence { get; set; }

    public string? MetadataLastError { get; set; }

    public DateTimeOffset? MetadataReviewedAt { get; set; }

    public long StateVersion { get; set; }

    public Guid? CurrentMetadataReviewOperationId { get; set; }
}
