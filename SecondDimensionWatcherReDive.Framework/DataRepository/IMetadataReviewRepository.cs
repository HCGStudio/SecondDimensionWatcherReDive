namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IMetadataReviewRepository
{
    Task<MetadataReviewQueuePage> GetQueueAsync(
        MetadataReviewStatus status,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task SavePreviewAsync(
        MetadataReviewPreviewDraft draft,
        CancellationToken cancellationToken);

    Task<MetadataReviewMutationResult> ApplyPreviewAsync(
        Guid operationId,
        Guid expectedAnimationInfoId,
        CancellationToken cancellationToken);

    Task<MetadataReviewMutationResult> UndoAsync(
        Guid operationId,
        long expectedVersion,
        CancellationToken cancellationToken);
}
