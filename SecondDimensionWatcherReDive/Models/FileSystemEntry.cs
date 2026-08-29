namespace SecondDimensionWatcherReDive.Models;

public sealed class FileSystemEntry
{
    public string Path { get; set; } = string.Empty;
    public string ParentPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public int DescendantFileCount { get; set; }
    public Guid? FileMappingId { get; set; }
    public FileMapping? FileMapping { get; set; }
}
