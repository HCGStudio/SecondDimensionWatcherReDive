using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageDownloadsParams>(
    "manage_downloads",
    "Control download tasks. Start, pause, resume, or cancel downloads for a specified animation.")]
internal sealed partial class ManageDownloadsTool(
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
    IFileDownloadClientProvider fileDownloadClientProvider,
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService) : ITool
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

        var downloadAttemptId = Guid.NewGuid();
        var submissionAttempted = false;
        try
        {
            if (!await animationInfoRepository.TryStartDownloadAsync(
                    info.Id,
                    downloadAttemptId,
                    DateTimeOffset.Now,
                    queuedDisposition: null,
                    cancellationToken))
                return new ToolFailureResult("Download already tracked");

            submissionAttempted = true;
            if (!await client.SubmitDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    cancellationToken))
            {
                await CompensateFailedStartAsync(
                    info,
                    client,
                    downloadAttemptId,
                    remoteMayHaveAccepted: false);
                return new ToolFailureResult("Download client rejected the task");
            }
        }
        catch
        {
            try
            {
                await CompensateFailedStartAsync(
                    info,
                    client,
                    downloadAttemptId,
                    submissionAttempted);
            }
            catch
            {
                // Preserve the initiating exception.
            }
            throw;
        }

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
        if (removeFile)
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal is null || !(await authorizationService.AuthorizeAsync(
                    principal, resource: null, AccessPolicies.RecentAdministrator)).Succeeded)
                return new ToolFailureResult("Deleting downloaded files requires recent administrator authentication");
        }

        var cancellationAttemptId = info.DownloadCancellationId ?? Guid.NewGuid();
        cancellationToken.ThrowIfCancellationRequested();
        using (var beginCancellation = CreateDownloadSagaTokenSource())
        {
            if (!await animationInfoRepository.TryBeginCancelDownloadAsync(
                    info.Id,
                    info.DownloadAttemptId,
                    cancellationAttemptId,
                    beginCancellation.Token))
                return new ToolFailureResult("Download state changed before cancellation");
        }

        var result = await client.CancelDownloadTaskAsync(
            info.Id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            removeFile,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return new ToolSuccessResult<bool>(false);
        }

        using var finalizeCancellation = CreateDownloadSagaTokenSource();
        var cancelled = await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
            info.Id,
            info.DownloadAttemptId,
            cancellationAttemptId,
            finalizeCancellation.Token);
        if (!cancelled)
            return new ToolFailureResult("Download state changed during cancellation");
        return new ToolSuccessResult<bool>(true);
    }

    private async Task CompensateFailedStartAsync(
        AnimationInfo info,
        IFileDownloadClient client,
        Guid downloadAttemptId,
        bool remoteMayHaveAccepted)
    {
        using var cleanup = CreateDownloadSagaTokenSource();
        if (remoteMayHaveAccepted)
        {
            try
            {
                var remoteCancellation = await client.CancelDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    removeFile: false,
                    cleanup.Token);
                if (!remoteCancellation.IsSuccess)
                {
                    await QueryDownloadProgressSafelyAsync(client, info, cleanup.Token);
                    return;
                }
            }
            catch
            {
                await QueryDownloadProgressSafelyAsync(client, info, cleanup.Token);
                return;
            }
        }

        await animationInfoRepository.TryCancelDownloadAsync(
            info.Id,
            downloadAttemptId,
            terminalDisposition: null,
            cleanup.Token);
    }

    private static async Task QueryDownloadProgressSafelyAsync(
        IFileDownloadClient client,
        AnimationInfo info,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.SubmitQueryDownloadProgressAsync(
                info.Id,
                info.DownloadUrl,
                info.CachedDownloadData,
                info.AdditionalDownloadInfo,
                cancellationToken);
        }
        catch
        {
            // Startup recovery can rediscover the persisted attempt.
        }
    }

    private static CancellationTokenSource CreateDownloadSagaTokenSource() =>
        new(TimeSpan.FromSeconds(10));
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
