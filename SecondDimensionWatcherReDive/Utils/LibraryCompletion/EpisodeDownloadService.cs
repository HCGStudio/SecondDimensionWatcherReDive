using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
namespace SecondDimensionWatcherReDive.Utils.LibraryCompletion;

// Uses the same persisted submission/cancellation saga as individual downloads.
public sealed class EpisodeDownloadService(IAnimationInfoRepository releases, IFileMappingRepository mappings,
    ILibraryCompletionRepository completion, IFileDownloadClientProvider clients,
    ILogger<EpisodeDownloadService> logger)
{
    public Task<CompletionSubmissionResult> SubmitAsync(AnimationInfo info, CancellationToken cancellationToken) =>
        SubmitCoreAsync(info, null, cancellationToken);

    public Task<CompletionSubmissionResult> SubmitAutomaticAsync(AnimationInfo info,
        MultiSourceSubscription subscription, CancellationToken cancellationToken) =>
        SubmitCoreAsync(info, subscription, cancellationToken);

    private async Task<CompletionSubmissionResult> SubmitCoreAsync(AnimationInfo info,
        MultiSourceSubscription? automaticSubscription, CancellationToken cancellationToken)
    {
        var episode = info.Episode!.Value;
        var tmdbId = info.Animation!.TmdbId;
        var season = info.Season!.Value;
        var claim = Guid.NewGuid();
        var claimOutcome = await completion.TryClaimEpisodeAsync(tmdbId, season, episode, info.Id, claim, cancellationToken);
        if (claimOutcome != EpisodeClaimOutcome.Acquired)
            return claimOutcome == EpisodeClaimOutcome.AlreadyPresentOrBusy
                ? new(episode, info.Id, "already_present_or_busy", true)
                : new(episode, info.Id, "candidate_unavailable", false);
        var attempt = Guid.NewGuid();
        var lease = Guid.NewGuid();
        IFileDownloadClient? client = null;
        var remoteMayHaveAccepted = false;
        try
        {
            client = clients.GetRequiredClient(info.DownloadType);
            var started = await releases.TryStartClaimedEpisodeDownloadAsync(info, claim, attempt, lease,
                TimeSpan.FromMinutes(3), DateTimeOffset.UtcNow, automaticSubscription, cancellationToken);
            if (started == null) return new(episode, info.Id, "state_changed", false);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(90));
            remoteMayHaveAccepted = true;
            var accepted = await client.SubmitDownloadTaskAsync(info.Id, info.DownloadUrl, info.CachedDownloadData,
                info.AdditionalDownloadInfo, budget.Token);
            if (!accepted)
            {
                remoteMayHaveAccepted = false;
                throw new InvalidOperationException("Download submission rejected.");
            }
            using var finalize = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (!await releases.TryMarkDownloadSubmittedAsync(info.Id, attempt, lease, finalize.Token))
                throw new InvalidOperationException("Download submission state changed.");
            return new(episode, info.Id, "submitted", true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Episode completion submission failed for {ReleaseId}", info.Id);
            if (client != null)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var cancellationId = Guid.NewGuid();
                    var cancellationLease = await releases.TryBeginCancelDownloadAsync(info.Id, attempt, cancellationId,
                        lease, TimeSpan.FromMinutes(3), false, true, SubscriptionAutomationDisposition.AutoDownloadFailed,
                        cleanup.Token);
                    if (cancellationLease != null)
                    {
                        var cancelled = !remoteMayHaveAccepted || (await client.CancelDownloadTaskAsync(info.Id,
                            info.DownloadUrl, info.CachedDownloadData, info.AdditionalDownloadInfo, false, cleanup.Token)).IsSuccess;
                        if (cancelled)
                            await mappings.TryFinalizeDownloadCancellationAsync(info.Id, attempt, cancellationId,
                                cancellationLease.Id, SubscriptionAutomationDisposition.AutoDownloadFailed, cleanup.Token);
                    }
                }
                catch (Exception cleanupError)
                {
                    logger.LogWarning(cleanupError, "Completion submission will be reconciled for {ReleaseId}", info.Id);
                }
            }
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            return new(episode, info.Id, "submission_failed", false);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await completion.ReleaseClaimAsync(tmdbId, season, episode, claim, cleanup.Token); }
            catch (Exception error) { logger.LogWarning(error, "Episode claim will expire for {ReleaseId}", info.Id); }
        }
    }
}
