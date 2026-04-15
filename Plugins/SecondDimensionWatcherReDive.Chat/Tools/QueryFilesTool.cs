using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<QueryFilesParams>(
    "query_files",
    "Query the file list of downloaded animations. Supports browsing subdirectories.")]
internal sealed partial class QueryFilesTool(
    IAnimationInfoRepository animationInfoRepository,
    IFileStoreProvider fileStoreProvider) : ITool
{
    private async Task<IToolResult> ExecuteCoreAsync(
        QueryFilesParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.AnimationId, out var animationId))
            return new ToolFailureResult("Invalid or missing animation_id");

        var info = await animationInfoRepository.FindByIdAsync(animationId, cancellationToken);
        if (info is null)
            return new ToolFailureResult("Animation not found");

        if (!info.IsDownloadFinished || info.FileStore is null || info.StorePath is null)
            return new ToolFailureResult("Download not finished or no file store configured");

        var store = fileStoreProvider.GetClient(info.FileStore);
        if (store is null)
            return new ToolFailureResult($"File store '{info.FileStore}' not found");

        var targetPath = param.RelativeDir is not null
            ? Path.Combine(info.StorePath, param.RelativeDir)
            : info.StorePath;

        var files = new List<FileSummary>();
        await foreach (var f in store.EnumerateDirectory(targetPath))
        {
            files.Add(new FileSummary(f.FileName, f.IsDirectory, f.Path));
        }

        return new ToolSuccessResult<FileListResult>(new FileListResult(info.Title, targetPath, files));
    }
}

internal sealed record QueryFilesParams(
    string AnimationId,
    string? RelativeDir = null);

internal sealed record FileListResult(string AnimationTitle, string Path, List<FileSummary> Files);
internal sealed record FileSummary(string FileName, bool IsDirectory, string Path);
