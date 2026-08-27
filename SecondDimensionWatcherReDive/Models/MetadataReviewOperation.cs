using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class MetadataReviewOperation
{
    public Guid Id { get; set; }

    public Guid AnimationInfoId { get; set; }

    public AnimationInfo AnimationInfo { get; set; } = null!;

    public MetadataReviewOperationState State { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public long BaseVersion { get; set; }

    public string? BaseFileStore { get; set; }

    public string? BaseStorePath { get; set; }

    public bool BaseIsDownloadFinished { get; set; }

    public string ProposedAnimationTmdbId { get; set; } = string.Empty;

    public string ProposedAnimationName { get; set; } = string.Empty;

    public string ProposedAnimationOriginalName { get; set; } = string.Empty;

    public string? ProposedAnimationPosterPath { get; set; }

    public string ProposedDescription { get; set; } = string.Empty;

    public int? ProposedSeason { get; set; }

    public int? ProposedEpisode { get; set; }

    public string? ProposedGroupName { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public DateTimeOffset? UndoneAt { get; set; }

    public long? AppliedVersion { get; set; }

    public string? PreviousDescription { get; set; }

    public Guid? PreviousAnimationId { get; set; }

    public Guid? PreviousGroupId { get; set; }

    public int? PreviousSeason { get; set; }

    public int? PreviousEpisode { get; set; }

    public MetadataReviewStatus? PreviousMetadataStatus { get; set; }

    public double? PreviousConfidence { get; set; }

    public string? PreviousLastError { get; set; }

    public bool? PreviousIsAiProcessed { get; set; }

    public int? PreviousAiRetryCount { get; set; }

    public DateTimeOffset? PreviousReviewedAt { get; set; }

    public Guid? PreviousCurrentOperationId { get; set; }

    public ICollection<MetadataReviewMappingSnapshot> MappingSnapshots { get; set; } = [];
}
