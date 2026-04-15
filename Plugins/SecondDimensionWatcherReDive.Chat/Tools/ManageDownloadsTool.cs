using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
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
    private async Task<IToolResult> ExecuteCoreAsync(
        ManageDownloadsParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.AnimationId, out var animationId))
            return new ToolFailureResult("Invalid or missing animation_id");

        var info = await animationInfoRepository.FindByIdAsync(animationId, cancellationToken);
        if (info is null)
            return new ToolFailureResult("Animation not found");

        var client = fileDownloadClientProvider.GetClient(info.DownloadType);
        if (client is null)
            return new ToolFailureResult($"No download client for type: {info.DownloadType}");

        return param.Action switch
        {
            ManageDownloadsAction.Start => await StartDownloadAsync(info, client, cancellationToken),
            ManageDownloadsAction.Pause => await PauseDownloadAsync(info, client, cancellationToken),
            ManageDownloadsAction.Resume => await ResumeDownloadAsync(info, client, cancellationToken),
            ManageDownloadsAction.Cancel => await CancelDownloadAsync(info, client, param.RemoveFile ?? false, cancellationToken),
            _ => new ToolFailureResult($"Unknown action: {param.Action}")
        };
    }

    private async Task<IToolResult> StartDownloadAsync(
        AnimationInfo info, IFileDownloadClient client, CancellationToken cancellationToken)
    {
        if (info.IsDownloadTracked)
            return new ToolFailureResult("Download already tracked");

        await client.SubmitDownloadTaskAsync(info.Id, info.DownloadUrl, info.CachedDownloadData,
            info.AdditionalDownloadInfo, cancellationToken);

        var updated = info with
        {
            IsDownloadTracked = true,
            DownloadStartTime = DateTimeOffset.Now
        };
        await animationInfoRepository.UpdateAsync(updated, cancellationToken);

        return new ToolSuccessResult<string>("Download started");
    }

    private async Task<IToolResult> PauseDownloadAsync(
        AnimationInfo info, IFileDownloadClient client, CancellationToken cancellationToken)
    {
        var success = await client.PauseDownloadTaskAsync(info.Id, info.DownloadUrl,
            info.CachedDownloadData, info.AdditionalDownloadInfo, cancellationToken);
        return new ToolSuccessResult<bool>(success);
    }

    private async Task<IToolResult> ResumeDownloadAsync(
        AnimationInfo info, IFileDownloadClient client, CancellationToken cancellationToken)
    {
        var success = await client.ResumeDownloadTaskAsync(info.Id, info.DownloadUrl,
            info.CachedDownloadData, info.AdditionalDownloadInfo, cancellationToken);
        return new ToolSuccessResult<bool>(success);
    }

    private async Task<IToolResult> CancelDownloadAsync(
        AnimationInfo info, IFileDownloadClient client, bool removeFile, CancellationToken cancellationToken)
    {
        var result = await client.CancelDownloadTaskAsync(info.Id, info.DownloadUrl,
            info.CachedDownloadData, info.AdditionalDownloadInfo, removeFile, cancellationToken);

        if (result.IsSuccess)
        {
            var updated = info with
            {
                IsDownloadTracked = false,
                IsDownloadFinished = false
            };
            await animationInfoRepository.UpdateAsync(updated, cancellationToken);
        }

        return new ToolSuccessResult<bool>(result.IsSuccess);
    }
}

internal enum ManageDownloadsAction
{
    Start,
    Pause,
    Resume,
    Cancel
}

internal sealed record ManageDownloadsParams(
    ManageDownloadsAction Action,
    string AnimationId,
    bool? RemoveFile = null);
