using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.FileStore;

/// <summary>
///     Request for renaming a single-episode download.
/// </summary>
public record FileRenameRequest(
    string AnimationName,
    int Season,
    int Episode,
    string StorePath,
    AnimationInfo AnimationInfo);

/// <summary>
///     Request for renaming multiple-episode downloads using AI inference.
/// </summary>
public record MultipleFileRenameRequest(
    string AnimationName,
    int Season,
    string OriginalTitle,
    string Path);

/// <summary>
///     Interface for renaming downloaded video files to a standardized naming format.
/// </summary>
public interface IFileRenamer
{
    /// <summary>
    ///     Renames a single-episode video file to the "Name SxxEyy [tag]" format.
    ///     For file-backed stores, also updates <see cref="AnimationInfo.StorePath" /> to the new path.
    /// </summary>
    Task RenameAsync(FileRenameRequest request, CancellationToken cancellationToken);

    /// <summary>
    ///     Renames multiple video files across one or more directories using AI inference to determine episode numbers.
    ///     Each file is renamed to the "Name SxxEyy [tag]" format independently.
    /// </summary>
    Task RenameMultipleAsync(MultipleFileRenameRequest request, CancellationToken cancellationToken);
}
