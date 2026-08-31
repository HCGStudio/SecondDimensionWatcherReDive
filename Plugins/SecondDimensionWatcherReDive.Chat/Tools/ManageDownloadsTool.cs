using System.Diagnostics;
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
    IFileMappingRepository fileMappingRepository,
    IFileDownloadClientProvider fileDownloadClientProvider) : ITool
{
    private static readonly TimeSpan DownloadSubmissionLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DownloadSubmissionRemoteBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadCancellationLeaseDuration = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DownloadCancellationRemoteBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan DownloadLeaseSafetyMargin = TimeSpan.FromSeconds(1);

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
        var submissionLeaseId = Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        var submissionAttempted = false;
        try
        {
            var submissionLease = await animationInfoRepository.TryStartDownloadAsync(
                    info.Id,
                    downloadAttemptId,
                    submissionLeaseId,
                    DownloadSubmissionLeaseDuration,
                    DateTimeOffset.Now,
                    queuedDisposition: null,
                    cancellationToken);
            if (submissionLease is null)
                return new ToolFailureResult("Download already tracked");

            var remainingRemoteBudget = DownloadSubmissionRemoteBudget -
                                        Stopwatch.GetElapsedTime(leaseRequestStartedAt);
            if (remainingRemoteBudget <= TimeSpan.Zero)
            {
                await CompensateFailedStartAsync(
                    info,
                    client,
                    downloadAttemptId,
                    submissionLeaseId,
                    remoteMayHaveAccepted: false);
                return new ToolFailureResult("Download submission lease expired");
            }

            using var submissionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            submissionCancellation.CancelAfter(remainingRemoteBudget);
            submissionCancellation.Token.ThrowIfCancellationRequested();
            submissionAttempted = true;
            if (!await client.SubmitDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    submissionCancellation.Token))
            {
                await CompensateFailedStartAsync(
                    info,
                    client,
                    downloadAttemptId,
                    submissionLeaseId,
                    remoteMayHaveAccepted: false);
                return new ToolFailureResult("Download client rejected the task");
            }

            using var markCancellation = CreateDownloadSagaTokenSource();
            if (!await animationInfoRepository.TryMarkDownloadSubmittedAsync(
                    info.Id,
                    downloadAttemptId,
                    submissionLeaseId,
                    markCancellation.Token))
            {
                await CompensateFailedStartAsync(
                    info,
                    client,
                    downloadAttemptId,
                    submissionLeaseId,
                    remoteMayHaveAccepted: true);
                return new ToolFailureResult("Download state changed during submission");
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
                    submissionLeaseId,
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
        var cancellationAttemptId = info.DownloadCancellationId ?? Guid.NewGuid();
        var cancellationLeaseId = Guid.NewGuid();
        var leaseRequestStartedAt = Stopwatch.GetTimestamp();
        DownloadCancellationLease? cancellationLease;
        cancellationToken.ThrowIfCancellationRequested();
        using (var beginCancellation = CreateDownloadSagaTokenSource())
        {
            cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
                    info.Id,
                    info.DownloadAttemptId,
                    cancellationAttemptId,
                    cancellationLeaseId,
                    DownloadCancellationLeaseDuration,
                    removeFile,
                    requireUnfinished: false,
                    SubscriptionAutomationDisposition.DownloadCancelled,
                    beginCancellation.Token);
            if (cancellationLease is null)
                return new ToolFailureResult("Download state changed before cancellation");
        }

        var remainingRemoteBudget = DownloadCancellationRemoteBudget -
                                    Stopwatch.GetElapsedTime(leaseRequestStartedAt);
        if (remainingRemoteBudget <= TimeSpan.Zero)
            return new ToolFailureResult("Download cancellation lease expired");
        using var remoteCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        remoteCancellation.CancelAfter(remainingRemoteBudget);
        remoteCancellation.Token.ThrowIfCancellationRequested();
        var result = await client.CancelDownloadTaskAsync(
            info.Id,
            info.DownloadUrl,
            info.CachedDownloadData,
            info.AdditionalDownloadInfo,
            cancellationLease.RemoveFile,
            remoteCancellation.Token);

        if (!result.IsSuccess)
        {
            return new ToolSuccessResult<bool>(false);
        }

        using var finalizeCancellation = CreateLeaseBoundSagaTokenSource(
            leaseRequestStartedAt,
            DownloadCancellationLeaseDuration);
        if (finalizeCancellation is null)
            return new ToolFailureResult("Download cancellation lease expired");
        var cancelled = await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
            info.Id,
            info.DownloadAttemptId,
            cancellationAttemptId,
            cancellationLease.Id,
            SubscriptionAutomationDisposition.DownloadCancelled,
            finalizeCancellation.Token);
        if (!cancelled)
            return new ToolFailureResult("Download state changed during cancellation");
        return new ToolSuccessResult<bool>(true);
    }

    private async Task CompensateFailedStartAsync(
        AnimationInfo info,
        IFileDownloadClient client,
        Guid downloadAttemptId,
        Guid submissionLeaseId,
        bool remoteMayHaveAccepted)
    {
        using var cleanup = CreateDownloadSagaTokenSource();
        var cancellationAttemptId = Guid.NewGuid();
        var cancellationLease = await animationInfoRepository.TryBeginCancelDownloadAsync(
            info.Id,
            downloadAttemptId,
            cancellationAttemptId,
            submissionLeaseId,
            DownloadCancellationLeaseDuration,
            removeFile: false,
            requireUnfinished: true,
            terminalDisposition: null,
            cleanup.Token);
        if (cancellationLease is null)
            return;

        if (remoteMayHaveAccepted)
        {
            try
            {
                cleanup.Token.ThrowIfCancellationRequested();
                var remoteCancellation = await client.CancelDownloadTaskAsync(
                    info.Id,
                    info.DownloadUrl,
                    info.CachedDownloadData,
                    info.AdditionalDownloadInfo,
                    cancellationLease.RemoveFile,
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

        cleanup.Token.ThrowIfCancellationRequested();
        await fileMappingRepository.TryFinalizeDownloadCancellationAsync(
            info.Id,
            downloadAttemptId,
            cancellationAttemptId,
            cancellationLease.Id,
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

    private static CancellationTokenSource? CreateLeaseBoundSagaTokenSource(
        long leaseRequestStartedAt,
        TimeSpan leaseDuration)
    {
        var remaining = leaseDuration -
                        Stopwatch.GetElapsedTime(leaseRequestStartedAt) -
                        DownloadLeaseSafetyMargin;
        if (remaining <= TimeSpan.Zero)
            return null;
        return new CancellationTokenSource(
            remaining < TimeSpan.FromSeconds(10) ? remaining : TimeSpan.FromSeconds(10));
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
