namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public enum MetadataReviewStatus
{
    Pending,
    Identified,
    LowConfidence,
    Failed,
    Reviewed
}

public enum MetadataReviewOperationState
{
    Draft,
    Applied,
    Undone,
    Expired
}

public enum MetadataReviewMappingKind
{
    Proposed,
    Previous
}

public enum MetadataReviewMutationOutcome
{
    Success,
    NotFound,
    Conflict,
    Expired
}

public sealed record MetadataReviewQueueItem(
    AnimationInfo Info,
    int MappedFileCount,
    Guid? CurrentOperationId,
    DateTimeOffset? CurrentOperationAppliedAt,
    bool CanUndo);

public sealed record MetadataReviewCounts(
    int Pending,
    int LowConfidence,
    int Failed);

public sealed record MetadataReviewOperationSummary(
    Guid OperationId,
    Guid AnimationInfoId,
    string Title,
    DateTimeOffset AppliedAt,
    long Revision,
    bool CanUndo);

public sealed record MetadataReviewQueuePage(
    IReadOnlyList<MetadataReviewQueueItem> Data,
    int TotalCount,
    MetadataReviewCounts Counts,
    IReadOnlyList<MetadataReviewOperationSummary> RecentOperations);

public sealed record MetadataReviewPreviewDraft(
    Guid Id,
    Guid AnimationInfoId,
    long BaseVersion,
    string? BaseFileStore,
    string? BaseStorePath,
    bool BaseIsDownloadFinished,
    Animation ProposedAnimation,
    string ProposedDescription,
    int? ProposedSeason,
    int? ProposedEpisode,
    string? ProposedGroupName,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<FileMapping> ProposedMappings);

public sealed record MetadataReviewMutationResult(
    MetadataReviewMutationOutcome Outcome,
    Guid OperationId,
    Guid? AnimationInfoId,
    long? Revision,
    DateTimeOffset? AppliedAt,
    IReadOnlyList<FileMapping> MappingsBefore,
    IReadOnlyList<FileMapping> MappingsAfter);
