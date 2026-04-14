using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
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
    private async Task<IToolExecutionResult> ExecuteCoreAsync(
        QueryFilesParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.AnimationId, out var animationId))
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Invalid or missing animation_id")), false);

        var info = await animationInfoRepository.FindByIdAsync(animationId, cancellationToken);
        if (info is null)
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Animation not found")), false);

        if (!info.IsDownloadFinished || info.FileStore is null || info.StorePath is null)
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Download not finished or no file store configured")), false);

        var store = fileStoreProvider.GetClient(info.FileStore);
        if (store is null)
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError($"File store '{info.FileStore}' not found")), false);

        var targetPath = param.RelativeDir is not null
            ? Path.Combine(info.StorePath, param.RelativeDir)
            : info.StorePath;

        var files = new List<FileSummary>();
        await foreach (var f in store.EnumerateDirectory(targetPath))
        {
            files.Add(new FileSummary(f.FileName, f.IsDirectory, f.Path));
        }

        return new ToolStringResult(ChatToolHelper.Serialize(new FileListResult(info.Title, targetPath, files)));
    }
}
