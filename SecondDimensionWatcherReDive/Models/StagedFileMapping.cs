namespace SecondDimensionWatcherReDive.Models;

/// <summary>
/// A completed alternative release's files before the release is validated and
/// atomically promoted into the live virtual-file-system namespace.
/// </summary>
public sealed class StagedFileMapping
{
    public Guid Id { get; set; }
    public Guid AnimationInfoId { get; set; }
    public string VirtualPath { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string FileStore { get; set; } = string.Empty;
}
