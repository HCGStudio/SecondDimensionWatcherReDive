namespace SecondDimensionWatcherReDive.Models;

public sealed class FileSystemDirectoryState
{
    public string Path { get; set; } = string.Empty;
    public long Generation { get; set; }
}
