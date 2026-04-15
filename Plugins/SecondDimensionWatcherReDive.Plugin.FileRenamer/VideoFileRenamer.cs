using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Plugin.FileRenamer;

public partial class VideoFileRenamer(
    IFileStore fileStore,
    IFileOperator fileOperator,
    ILogger<VideoFileRenamer> logger,
    IInferenceEngine? inferenceEngine = null) : IFileRenamer
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".flv", ".wmv", ".webm"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt"
    };

    public async Task RenameAsync(FileRenameContext context, CancellationToken cancellationToken)
    {
        if (!await fileStore.ExistAsync(context.StorePath, cancellationToken))
        {
            LogStorePathNotExist(logger, context.StorePath);
            return;
        }

        var animationName = SanitizeFileName(context.AnimationName);

        var videoFiles = new List<FileStoreInfo>();
        var allFiles = new List<FileStoreInfo>();
        await foreach (var file in fileStore.EnumerateDirectory(context.StorePath))
        {
            if (!file.IsDirectory)
            {
                allFiles.Add(file);
                if (VideoExtensions.Contains(Path.GetExtension(file.FileName)))
                    videoFiles.Add(file);
            }
        }

        if (videoFiles.Count == 0)
        {
            LogNoVideoFiles(logger, context.StorePath);
            return;
        }

        if (context.Episode != null)
        {
            await RenameSingleEpisode(videoFiles, allFiles, animationName, context.Season, context.Episode.Value, cancellationToken);
        }
        else
        {
            await RenameMultipleEpisodes(
                videoFiles, allFiles, animationName, context.Season, context.OriginalTitle, cancellationToken);
        }
    }

    private async Task RenameSingleEpisode(
        List<FileStoreInfo> videoFiles, List<FileStoreInfo> allFiles,
        string animationName, int season, int episode, CancellationToken cancellationToken)
    {
        // If multiple video files, pick the largest one as the main video
        var target = videoFiles.Count == 1
            ? videoFiles[0]
            : videoFiles.OrderByDescending(f => new FileInfo(f.Path).Length).First();

        var ext = Path.GetExtension(target.FileName);
        var newName = FormatFileName(animationName, season, episode, ext);
        var newPath = Path.Combine(Path.GetDirectoryName(target.Path)!, newName);

        if (target.Path != newPath)
        {
            var success = await fileOperator.RenameAsync(target.Path, newPath, cancellationToken);
            if (success)
                LogRenamed(logger, target.FileName, newName);
            else
                LogRenameFailed(logger, target.FileName, newName);
        }

        var newBaseName = FormatFileName(animationName, season, episode, "");
        await RenameMatchingSubtitles(target.FileName, newBaseName, allFiles, cancellationToken);
    }

    private async Task RenameMultipleEpisodes(
        List<FileStoreInfo> videoFiles, List<FileStoreInfo> allFiles,
        string animationName, int season,
        string originalTitle, CancellationToken cancellationToken)
    {
        if (inferenceEngine == null)
        {
            LogNoInferenceEngine(logger);
            return;
        }

        foreach (var file in videoFiles)
        {
            var result = await inferenceEngine.InferAsync(file.FileName, originalTitle, cancellationToken);

            if (result?.Episode == null)
            {
                LogCouldNotInferEpisode(logger, file.FileName);
                continue;
            }

            var ext = Path.GetExtension(file.FileName);
            var inferredSeason = result.Season ?? season;
            var newName = FormatFileName(animationName, inferredSeason, result.Episode.Value, ext);
            var newPath = Path.Combine(Path.GetDirectoryName(file.Path)!, newName);

            if (file.Path != newPath)
            {
                var success = await fileOperator.RenameAsync(file.Path, newPath, cancellationToken);
                if (success)
                    LogRenamed(logger, file.FileName, newName);
                else
                    LogRenameFailed(logger, file.FileName, newName);
            }

            var newBaseName = FormatFileName(animationName, inferredSeason, result.Episode.Value, "");
            await RenameMatchingSubtitles(file.FileName, newBaseName, allFiles, cancellationToken);
        }
    }

    /// <summary>
    ///     Finds subtitle files that share the same base name as the video file and renames them
    ///     to match the new base name, preserving any language suffix (e.g. ".zh.srt", ".chs.ass").
    /// </summary>
    private async Task RenameMatchingSubtitles(
        string videoFileName, string newBaseName, List<FileStoreInfo> allFiles, CancellationToken cancellationToken)
    {
        var videoBase = Path.GetFileNameWithoutExtension(videoFileName);

        foreach (var file in allFiles)
        {
            var ext = Path.GetExtension(file.FileName);
            if (!SubtitleExtensions.Contains(ext)) continue;

            // Check if the subtitle base name starts with the video base name.
            // This matches both "video.srt" and "video.zh.srt" / "video.chs.ass".
            var subtitleBase = Path.GetFileNameWithoutExtension(file.FileName);
            if (!subtitleBase.Equals(videoBase, StringComparison.OrdinalIgnoreCase)
                && !subtitleBase.StartsWith(videoBase + ".", StringComparison.OrdinalIgnoreCase))
                continue;

            // Preserve language/tag suffix: "video.zh" -> suffix is ".zh"
            var suffix = subtitleBase.Length > videoBase.Length
                ? subtitleBase[videoBase.Length..]
                : "";

            var newSubName = newBaseName + suffix + ext;
            var newSubPath = Path.Combine(Path.GetDirectoryName(file.Path)!, newSubName);

            if (file.Path == newSubPath) continue;

            var success = await fileOperator.RenameAsync(file.Path, newSubPath, cancellationToken);
            if (success)
                LogSubtitleRenamed(logger, file.FileName, newSubName);
            else
                LogSubtitleRenameFailed(logger, file.FileName, newSubName);
        }
    }

    private static string FormatFileName(string animationName, int season, int episode, string ext)
    {
        return $"{animationName} S{season:D2}E{episode:D2}{ext}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Store path does not exist: {StorePath}")]
    private static partial void LogStorePathNotExist(ILogger logger, string storePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No video files found in {StorePath}")]
    private static partial void LogNoVideoFiles(ILogger logger, string storePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed: {Old} -> {New}")]
    private static partial void LogRenamed(ILogger logger, string old, string @new);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rename: {Old} -> {New}")]
    private static partial void LogRenameFailed(ILogger logger, string old, string @new);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot determine episode numbers for multi-episode torrent without inference engine")]
    private static partial void LogNoInferenceEngine(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not infer episode for file: {FileName}")]
    private static partial void LogCouldNotInferEpisode(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed subtitle: {Old} -> {New}")]
    private static partial void LogSubtitleRenamed(ILogger logger, string old, string @new);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rename subtitle: {Old} -> {New}")]
    private static partial void LogSubtitleRenameFailed(ILogger logger, string old, string @new);
}
