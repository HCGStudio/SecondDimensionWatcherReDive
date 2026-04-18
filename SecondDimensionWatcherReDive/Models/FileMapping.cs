namespace SecondDimensionWatcherReDive.Models;

public class FileMapping
{
    public Guid Id { get; set; }
    public Guid AnimationInfoId { get; set; }
    public string VirtualPath { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string FileStore { get; set; } = string.Empty;
}
