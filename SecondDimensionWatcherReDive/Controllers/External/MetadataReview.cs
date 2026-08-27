namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record MetadataReviewQueueResponse(
    IReadOnlyList<MetadataReviewItem> Data,
    int TotalItems,
    MetadataReviewCounts Counts,
    IReadOnlyList<MetadataReviewRecentOperation> RecentOperations);

internal sealed record MetadataReviewCounts(
    int Pending,
    int LowConfidence,
    int Failed);

internal sealed record MetadataReviewItem(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset PublishTime,
    string ReviewStatus,
    double? Confidence,
    string? FailureReason,
    int AiRetryCount,
    MetadataReviewMetadata Metadata,
    bool IsDownloadFinished,
    int MappedFileCount,
    long Revision,
    Guid? CurrentOperationId,
    DateTimeOffset? CurrentOperationAppliedAt,
    bool CanUndo);

internal sealed record MetadataReviewMetadata(
    string? TmdbId,
    string? Name,
    string? OriginalName,
    string? PosterPath,
    int? Season,
    int? Episode,
    string? GroupName);

internal sealed record MetadataReviewRecentOperation(
    Guid OperationId,
    Guid ItemId,
    string Title,
    DateTimeOffset AppliedAt,
    long Revision,
    bool CanUndo);

internal sealed record MetadataReviewDraft(
    string? TmdbId,
    int? Season,
    int? Episode,
    string? GroupName);

internal sealed record MetadataReviewPreviewRequest(
    long ExpectedRevision,
    MetadataReviewDraft Metadata);

internal sealed record MetadataReviewApplyRequest(Guid PreviewId);

internal sealed record MetadataReviewUndoRequest(long ExpectedRevision);

internal sealed record MetadataReviewPreviewResponse(
    Guid PreviewId,
    long BaseRevision,
    MetadataReviewMetadata ResolvedMetadata,
    IReadOnlyList<MetadataReviewPathChange> PathChanges,
    IReadOnlyList<string> Warnings,
    bool CanApply,
    DateTimeOffset ExpiresAt);

internal sealed record MetadataReviewPathChange(
    string FileName,
    string? CurrentVirtualPath,
    string? ProposedVirtualPath,
    string ChangeKind,
    bool CollisionAdjusted);

internal sealed record MetadataReviewMutationResponse(
    Guid OperationId,
    long Revision,
    IReadOnlyList<MetadataReviewPathChange> PathChanges,
    DateTimeOffset AppliedAt,
    bool CanUndo);

internal sealed record MetadataReviewError(string Code, string Message);
