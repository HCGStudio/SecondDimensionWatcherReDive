using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageDownloadsParams>(
    "manage_downloads",
    "Control download tasks. Start, pause, resume, or cancel downloads for a specified animation.")]
internal sealed partial class ManageDownloadsTool(
    IAnimationInfoRepository animationInfoRepository,
    IFileDownloadClientProvider fileDownloadClientProvider) : ITool
{
    private async Task<IToolExecutionResult> ExecuteCoreAsync(
        ManageDownloadsParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.AnimationId, out var animationId))
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Invalid or missing animation_id")), false);

        var info = await animationInfoRepository.FindByIdAsync(animationId, cancellationToken);
        if (info is null)
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Animation not found")), false);

        var client = fileDownloadClientProvider.GetClient(info.DownloadType);
        if (client is null)
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError($"No download client for type: {info.DownloadType}")), false);

        var result = param.Action switch
        {
            ManageDownloadsAction.Start => await StartDownloadAsync(info, client, cancellationToken),
            ManageDownloadsAction.Pause => await PauseDownloadAsync(info, client),
            ManageDownloadsAction.Resume => await ResumeDownloadAsync(info, client),
            ManageDownloadsAction.Cancel => await CancelDownloadAsync(info, client, param.RemoveFile ?? false, cancellationToken),
            _ => ChatToolHelper.Serialize(new ToolError($"Unknown action: {param.Action}"))
        };
        return new ToolStringResult(result);
    }

    private async Task<string> StartDownloadAsync(
        AnimationInfo info, IFileDownloadClient client, CancellationToken cancellationToken)
    {
        if (info.IsDownloadTracked)
            return ChatToolHelper.Serialize(new ToolError("Download already tracked"));

        await client.SubmitDownloadTask(info.Id, info.DownloadUrl, info.CachedDownloadData,
            info.AdditionalDownloadInfo);

        var updated = info with
        {
            IsDownloadTracked = true,
            DownloadStartTime = DateTimeOffset.Now
        };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);

        return ChatToolHelper.Serialize(new ToolSuccess(true, "Download started"));
    }

    private async Task<string> PauseDownloadAsync(AnimationInfo info, IFileDownloadClient client)
    {
        var result = await client.PauseDownloadTask(info.Id, info.DownloadUrl,
            info.CachedDownloadData, info.AdditionalDownloadInfo);
        return ChatToolHelper.Serialize(new ToolSuccess(result));
    }

    private async Task<string> ResumeDownloadAsync(AnimationInfo info, IFileDownloadClient client)
    {
        var result = await client.ResumeDownloadTask(info.Id, info.DownloadUrl,
            info.CachedDownloadData, info.AdditionalDownloadInfo);
        return ChatToolHelper.Serialize(new ToolSuccess(result));
    }

    private async Task<string> CancelDownloadAsync(
        AnimationInfo info, IFileDownloadClient client, bool removeFile, CancellationToken cancellationToken)
    {
        var result = await client.CancelDownloadTask(info.Id, info.DownloadUrl,
            info.CachedDownloadData, info.AdditionalDownloadInfo, removeFile);

        if (result.IsSuccess)
        {
            var updated = info with
            {
                IsDownloadTracked = false,
                IsDownloadFinished = false
            };
            await animationInfoRepository.UpdateAsync(updated, cancellationToken);
        }

        return ChatToolHelper.Serialize(new ToolSuccess(result.IsSuccess));
    }
}
