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
        var coordinator = scope.ServiceProvider.GetRequiredService<IReleaseUpgradeCoordinator>();
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
