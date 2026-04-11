namespace SecondDimensionWatcherReDive.Framework.FileStore;

/// <summary>
///     Data needed to rename downloaded video files.
/// </summary>
public record FileRenameContext(
    string AnimationName,
    int Season,
    int? Episode,
    string OriginalTitle,
    string StorePath);

/// <summary>
///     Interface for renaming downloaded video files to a standardized naming format.
/// </summary>
public interface IFileRenamer
{
    /// <summary>
    ///     Renames video files to the "Name SxxEyy" format.
    /// </summary>
    Task RenameAsync(FileRenameContext context, CancellationToken cancellationToken);
}
