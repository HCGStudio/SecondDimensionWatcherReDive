using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Plugin.FileRenamer;

public class VideoFileRenamer(
    IFileStore fileStore,
    IFileOperator fileOperator,
    ILogger<VideoFileRenamer> logger,
    IInferenceEngine? inferenceEngine = null) : IFileRenamer
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".flv", ".wmv", ".webm"
    };

    public async Task RenameAsync(FileRenameContext context, CancellationToken cancellationToken)
    {
        if (!await fileStore.Exist(context.StorePath))
        {
            logger.LogWarning("Store path does not exist: {StorePath}", context.StorePath);
            return;
        }

        var animationName = SanitizeFileName(context.AnimationName);

        var videoFiles = new List<FileStoreInfo>();
        await foreach (var file in fileStore.EnumerateDirectory(context.StorePath))
        {
            if (!file.IsDirectory && VideoExtensions.Contains(Path.GetExtension(file.FileName)))
                videoFiles.Add(file);
        }

        if (videoFiles.Count == 0)
        {
            logger.LogDebug("No video files found in {StorePath}", context.StorePath);
            return;
        }

        if (context.Episode != null)
        {
            await RenameSingleEpisode(videoFiles, animationName, context.Season, context.Episode.Value);
        }
        else
        {
            await RenameMultipleEpisodes(
                videoFiles, animationName, context.Season, context.OriginalTitle, cancellationToken);
        }
    }

    private async Task RenameSingleEpisode(
        List<FileStoreInfo> videoFiles, string animationName, int season, int episode)
    {
        // If multiple video files, pick the largest one as the main video
        var target = videoFiles.Count == 1
            ? videoFiles[0]
            : videoFiles.OrderByDescending(f => new FileInfo(f.Path).Length).First();

        var ext = Path.GetExtension(target.FileName);
        var newName = FormatFileName(animationName, season, episode, ext);
        var newPath = Path.Combine(Path.GetDirectoryName(target.Path)!, newName);

        if (target.Path == newPath) return;

        var success = await fileOperator.Rename(target.Path, newPath);
        if (success)
            logger.LogInformation("Renamed: {Old} -> {New}", target.FileName, newName);
        else
            logger.LogWarning("Failed to rename: {Old} -> {New}", target.FileName, newName);
    }

    private async Task RenameMultipleEpisodes(
        List<FileStoreInfo> videoFiles, string animationName, int season,
        string originalTitle, CancellationToken cancellationToken)
    {
        if (inferenceEngine == null)
        {
            logger.LogWarning(
                "Cannot determine episode numbers for multi-episode torrent without inference engine");
            return;
        }

        foreach (var file in videoFiles)
        {
            var result = await inferenceEngine.InferAsync(file.FileName, originalTitle, cancellationToken);

            if (result?.Episode == null)
            {
                logger.LogWarning("Could not infer episode for file: {FileName}", file.FileName);
                continue;
            }

            var ext = Path.GetExtension(file.FileName);
            var inferredSeason = result.Season ?? season;
            var newName = FormatFileName(animationName, inferredSeason, result.Episode.Value, ext);
            var newPath = Path.Combine(Path.GetDirectoryName(file.Path)!, newName);

            if (file.Path == newPath) continue;

            var success = await fileOperator.Rename(file.Path, newPath);
            if (success)
                logger.LogInformation("Renamed: {Old} -> {New}", file.FileName, newName);
            else
                logger.LogWarning("Failed to rename: {Old} -> {New}", file.FileName, newName);
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
}
