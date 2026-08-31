using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class AnimationInfo
{
    public Guid Id { get; set; }
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public DateTimeOffset PublishTime { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;

    public string DownloadType { get; set; } = string.Empty;

    public byte[] CachedDownloadData { get; set; } = Array.Empty<byte>();

    public string AdditionalDownloadInfo { get; set; } = string.Empty;

    public bool IsDownloadTracked { get; set; }

    public Guid? DownloadAttemptId { get; set; }

    public Guid? DownloadSubmissionLeaseId { get; set; }

    public DateTimeOffset? DownloadSubmissionLeaseUntil { get; set; }

    public Guid? DownloadCancellationId { get; set; }

    public Guid? DownloadCancellationLeaseId { get; set; }

    public DateTimeOffset? DownloadCancellationLeaseUntil { get; set; }

    public bool DownloadCancellationRemoveFile { get; set; }

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

    public Guid? MediaLibrarySourceId { get; set; }

    public DateTimeOffset? MediaLibraryMissingSince { get; set; }

    public string? ReleaseIdentity { get; set; }

    public string? FeedItemGuid { get; set; }

    public string? EnclosureId { get; set; }

    public string? TorrentInfoHash { get; set; }

    public string? ReleaseSubtitleGroup { get; set; }

    public string? ReleaseResolution { get; set; }

    public string? ReleaseCodec { get; set; }

    public string[] ReleaseLanguages { get; set; } = [];

    public int ReleaseScore { get; set; }

    public string? ReleaseScoreReasonsJson { get; set; }

    public int? ExpectedEpisodeCount { get; set; }

    public bool IsActiveRelease { get; set; }

    /// <summary>
    /// The release was deliberately removed from the live episode namespace by
    /// a successful replacement. This remains durable after upgrade history is
    /// deleted with either referenced release.
    /// </summary>
    public bool IsRetiredRelease { get; set; }
}
