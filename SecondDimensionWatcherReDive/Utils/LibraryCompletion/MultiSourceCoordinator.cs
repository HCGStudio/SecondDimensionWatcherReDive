using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;
namespace SecondDimensionWatcherReDive.Utils.LibraryCompletion;

public sealed class MultiSourceCoordinator(IMultiSourceSubscriptionRepository subscriptions,
    ILibraryCompletionRepository releases, LibraryCompletionService completion, EpisodeDownloadService downloads,
    IReleaseUpgradeRepository upgrades, IReleaseUpgradeCoordinator upgradeCoordinator,
    INotificationPublisher notifications)
{
    public async Task<CompletionSubmissionResult?> ConfirmAsync(MultiSourceSubscription subscription,
        int episode, CancellationToken cancellationToken)
    {
        var currentSubscription = (await subscriptions.GetAllAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == subscription.Id);
        if (currentSubscription?.Mode != "ManualConfirm") return null;
        subscription = currentSubscription;
        await EvaluateAsync(subscription, cancellationToken);
        var decision = (await subscriptions.GetDecisionsAsync(subscription.Id, cancellationToken))
            .FirstOrDefault(x => x.Episode == episode && x.Outcome == "pending_confirmation");
        if (decision?.SelectedReleaseId is not { } releaseId
            || decision.SelectedSourceFeedId is not { } sourceFeedId
            || !subscription.FeedIds.Contains(sourceFeedId)) return null;
        var result = await completion.SubmitAsync(new(subscription.TmdbId, subscription.Season,
            [new(episode, releaseId)]), cancellationToken);
        await EvaluateAsync(subscription, cancellationToken);
        return result.Single();
    }

    public async Task EvaluateAsync(MultiSourceSubscription subscription, CancellationToken cancellationToken,
        bool retryFailures = false)
    {
        var all = await releases.GetSeasonReleasesAsync(subscription.TmdbId, subscription.Season, cancellationToken);
        var mapped = await releases.GetMappedReleaseIdsAsync(subscription.TmdbId, subscription.Season, cancellationToken);
        var previous = (await subscriptions.GetDecisionsAsync(subscription.Id, cancellationToken)).ToDictionary(x => x.Episode);
        var linked = all.Where(x => x.SourceFeedId is { } id && subscription.FeedIds.Contains(id) && LibraryCompletionService.IsReliable(x));
        foreach (var group in linked.GroupBy(x => x.Episode!.Value).OrderBy(x => x.Key))
        {
            var now = DateTimeOffset.UtcNow;
            var old = previous.GetValueOrDefault(group.Key);
            // Failed decisions are terminal for background evaluation, including while a failed
            // submission is still being compensated. Only an explicit user retry reopens them.
            if (old?.Outcome == "failed" && !retryFailures) continue;
            var firstSeen = group.Min(x => x.IngestedAt ?? x.PublishTime);
            if (firstSeen < subscription.CreatedAt) firstSeen = subscription.CreatedAt;
            var started = old?.WaitStartedAt ?? firstSeen;
            var until = started.AddMinutes(subscription.WaitMinutes);
            var eligible = group.Select(x => (Info: x, Candidate: completion.Candidate(x, subscription.ToPolicy(x.SourceFeedId!.Value))))
                .Where(x => x.Candidate.Eligible).OrderBy(x => subscription.FeedIds.ToList().IndexOf(x.Info.SourceFeedId!.Value))
                .ThenByDescending(x => x.Candidate.Score).ThenByDescending(x => x.Info.PublishTime).ThenBy(x => x.Info.Id).ToList();
            var current = all.FirstOrDefault(x => x.Episode == group.Key && x.IsDownloadFinished && mapped.Contains(x.Id));
            var downloading = all.FirstOrDefault(x => x.Episode == group.Key && x.IsDownloadTracked && !x.IsDownloadFinished);
            var pendingMappings = all.Where(x => x.Episode == group.Key && x.IsDownloadFinished && !mapped.Contains(x.Id));
            var activeUpgrade = false;
            if (downloading is null)
            {
                foreach (var pending in pendingMappings)
                {
                    // Failed activation may retain staged files indefinitely. It
                    // must not hide a playable incumbent or block later upgrades.
                    activeUpgrade = await upgrades.FindActiveByCandidateAsync(pending.Id, cancellationToken) is not null;
                    if (current is not null && !activeUpgrade) continue;
                    downloading = pending;
                    break;
                }
            }
            var selected = eligible.FirstOrDefault();
            var outcome = "waiting";
            var reason = "waiting_for_primary";
            Guid? selectedId = selected.Info?.Id;
            if (downloading != null)
            {
                selectedId = downloading.Id;
                outcome = activeUpgrade ? "upgrading" : downloading.IsDownloadFinished ? "mapping_pending" : "downloading";
                reason = activeUpgrade ? "upgrade_in_progress" : downloading.IsDownloadFinished ? "mapping_pending" : "episode_already_downloading";
            }
            else if (current != null)
            {
                selectedId = current.Id; outcome = "downloaded"; reason = "existing_release_retained";
                if (subscription.Mode == "AutoDownload" && subscription.EnableVersionUpgrade && selected.Info != null)
                {
                    var retainFallbackSource = current.SourceFeedId is { } currentFeedId
                        && subscription.FeedIds.Skip(1).Contains(currentFeedId);
                    selected = eligible.Where(x => !retainFallbackSource || x.Info.SourceFeedId == current.SourceFeedId)
                        .OrderByDescending(x => x.Candidate.Score)
                        .ThenBy(x => subscription.FeedIds.ToList().IndexOf(x.Info.SourceFeedId!.Value)).FirstOrDefault();
                    var candidate = selected.Info is null ? null
                        : await upgrades.FindCandidateAsync(current.Id, selected.Info.Id, cancellationToken);
                    if (candidate is { Automatic: true } && candidate.CandidateScore - candidate.CurrentScore >= subscription.MinimumUpgradeScore)
                    {
                        var result = await upgradeCoordinator.ExecuteAsync(candidate, ReleaseUpgradeInvocation.AutomaticMultiSource, false, cancellationToken);
                        // A competing evaluation or changed eligibility can reject
                        // the claim. Preserve the winner's decision and let the
                        // next pass observe current state instead of failing it.
                        if (result.Outcome == "upgrade_already_started") continue;
                        selectedId = candidate.CandidateReleaseId; outcome = result.IsSuccess ? "upgrading" : "failed"; reason = result.Outcome;
                    }
                    else reason = selected.Info is null ? "existing_release_retained" : "upgrade_threshold_not_met";
                }
            }
            else if (selected.Info == null)
            {
                outcome = "unavailable"; reason = "no_eligible_candidate";
            }
            else if (selected.Info.SourceFeedId == subscription.FeedIds.FirstOrDefault() || now >= until)
            {
                reason = selected.Info.SourceFeedId == subscription.FeedIds.FirstOrDefault() ? "primary_available" : "primary_wait_expired";
                outcome = subscription.Mode switch { "AutoDownload" => "ready", "NotifyOnly" => "notified", _ => "pending_confirmation" };
                if (subscription.Mode == "AutoDownload")
                {
                    var result = await downloads.SubmitAutomaticAsync(selected.Info, subscription, cancellationToken);
                    // A changed subscription or release invalidates this evaluation;
                    // let the next pass use current rules instead of recording a failure.
                    if (result.Outcome == "state_changed") return;
                    outcome = result.IsSuccess ? "downloading" : "failed";
                    if (result.Outcome == "already_present_or_busy")
                    {
                        var actual = (await releases.GetSeasonReleasesAsync(subscription.TmdbId, subscription.Season, cancellationToken))
                            .FirstOrDefault(x => x.Episode == group.Key && (x.IsDownloadTracked || x.IsDownloadFinished));
                        selectedId = actual?.Id;
                        outcome = actual?.IsDownloadFinished == true ? mapped.Contains(actual.Id) ? "downloaded" : "mapping_pending" : actual != null ? "downloading" : "waiting";
                        reason = "episode_already_downloading";
                    }
                    if (!result.IsSuccess) reason = result.Outcome;
                }
            }
            var persisted = await subscriptions.SaveDecisionAsync(new(subscription.Id, group.Key, started, until, selectedId,
                outcome, reason, now), subscription, cancellationToken);
            if (persisted is null) continue;
            if (outcome is "notified" or "pending_confirmation" && (old?.Outcome != outcome || old.SelectedReleaseId != selectedId))
                await notifications.PublishAsync(new NotificationEvent(
                    outcome == "notified" ? NotificationEventType.ReleaseMatched : NotificationEventType.DownloadPendingConfirmation,
                    $"multi-source:{subscription.Id}:{group.Key}:{selectedId}:{outcome}", subscription.Name,
                    $"S{subscription.Season}E{group.Key}: {selected.Info?.Title}", "/feeds"), cancellationToken);
        }
    }
}

public sealed class MultiSourceBackgroundService(IServiceScopeFactory scopeFactory,
    ILogger<MultiSourceBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IMultiSourceSubscriptionRepository>();
                var coordinator = scope.ServiceProvider.GetRequiredService<MultiSourceCoordinator>();
                foreach (var subscription in await repository.GetAllAsync(stoppingToken))
                {
                    try { await coordinator.EvaluateAsync(subscription, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception error) { logger.LogWarning(error, "Multi-source subscription {Id} evaluation failed", subscription.Id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "Multi-source subscriptions unavailable"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
