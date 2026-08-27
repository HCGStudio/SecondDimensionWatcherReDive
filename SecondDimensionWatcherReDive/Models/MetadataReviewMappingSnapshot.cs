using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class MetadataReviewMappingSnapshot
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public MetadataReviewOperation Operation { get; set; } = null!;

    public MetadataReviewMappingKind Kind { get; set; }

    public string VirtualPath { get; set; } = string.Empty;

    public string PhysicalPath { get; set; } = string.Empty;

    public string FileStore { get; set; } = string.Empty;
}
