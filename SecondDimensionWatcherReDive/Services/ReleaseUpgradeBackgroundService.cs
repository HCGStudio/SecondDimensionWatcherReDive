using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

namespace SecondDimensionWatcherReDive.Services;

public sealed class ReleaseUpgradeBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReleaseUpgradeBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic release-upgrade pass failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReleaseUpgradeRepository>();
        await repository.CompleteExpiredAsync(DateTimeOffset.UtcNow, cancellationToken);
        await repository.ClearExpiredDownloadSubmissionLeasesWithoutCancellationAsync(
            cancellationToken);
        var coordinator = scope.ServiceProvider.GetRequiredService<IReleaseUpgradeCoordinator>();
        var animationInfoRepository = scope.ServiceProvider
            .GetRequiredService<IAnimationInfoRepository>();
        var pendingDownloadSubmissions =
            await animationInfoRepository.GetPendingDownloadSubmissionsAsync(
                take: 20,
                cancellationToken);
        foreach (var pendingSubmission in pendingDownloadSubmissions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await coordinator.ReconcilePendingDownloadSubmissionAsync(
                    pendingSubmission,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Download submission recovery failed for animation {AnimationInfoId}",
                    pendingSubmission.AnimationInfoId);
            }
        }

        var pendingCancellations = await repository.GetPendingDownloadCancellationsAsync(
            take: 20,
            cancellationToken);
        foreach (var pendingCancellation in pendingCancellations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await coordinator.ReconcilePendingDownloadCancellationAsync(
                    pendingCancellation,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Release-upgrade cancellation reconciliation failed for operation {OperationId}",
                    pendingCancellation.OperationId);
            }
        }

        var pendingDownloadCancellations =
            await animationInfoRepository.GetPendingDownloadCancellationsAsync(
                take: 20,
                cancellationToken);
        foreach (var pendingCancellation in pendingDownloadCancellations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await coordinator.ReconcilePendingDownloadCancellationAsync(
                    pendingCancellation,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Download cancellation reconciliation failed for animation {AnimationInfoId}",
                    pendingCancellation.AnimationInfoId);
            }
        }

        var readyCandidates = await repository.GetReadyCandidateIdsAsync(
            take: 20,
            cancellationToken);
        foreach (var candidateId in readyCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await coordinator.TryActivateCandidateAsync(candidateId, cancellationToken);
        }

        var candidates = await repository.GetCandidatesAsync(
            automaticOnly: true,
            take: 20,
            cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await coordinator.ExecuteAsync(candidate, dryRun: false, cancellationToken);
        }
    }
}
