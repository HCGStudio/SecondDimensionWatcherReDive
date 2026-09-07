using System.Collections.Concurrent;
using System.Diagnostics;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal sealed partial class HlsTranscodingService
{
    private sealed record CacheReadLease(Guid Id, long ValidUntil)
    {
        public bool IsValid => Id == Guid.Empty || Stopwatch.GetTimestamp() < ValidUntil;
    }

    private readonly ConcurrentDictionary<string, CacheReadLease> _readerLeases = new(StringComparer.Ordinal);

    // The caller holds _creationGate. Reading the manifest and registering its
    // readers share the eviction lock, so a stale local Ready job cannot revive
    // an already deleted cache. Read leases reserve no additional disk capacity.
    private async Task<CacheManifest?> TryLoadProtectedManifestAsync(string directory, CancellationToken cancellationToken)
    {
        var key = Path.GetFileName(directory);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var capacity = scope.ServiceProvider.GetService<IDownloadCapacityRepository>();
        if (capacity is null)
        {
            var local = await TryLoadManifestAsync(directory, cancellationToken);
            if (local is not null) _readerLeases[key] = new CacheReadLease(Guid.Empty, long.MaxValue);
            return local;
        }

        await using var transaction = await capacity.BeginAsync(cancellationToken);
        var manifest = await TryLoadManifestAsync(directory, cancellationToken);
        if (manifest is null) return null;
        var id = _readerLeases.TryGetValue(key, out var previous) ? previous.Id : Guid.NewGuid();
        var validUntil = Stopwatch.GetTimestamp() + TranscodeCapacityService.LeaseSeconds * Stopwatch.Frequency;
        await scope.ServiceProvider.GetRequiredService<ITranscodeCapacityRepository>()
            .RegisterReaderAsync(id, CapacityVolume.CanonicalPath(directory), TranscodeCapacityService.LeaseSeconds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _readerLeases[key] = new CacheReadLease(id, validUntil);
        return manifest;
    }

    private async Task RunReaderLeaseLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    await RefreshReaderLeasesAsync(releaseAll: false, deadline.Token);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not renew shared transcoding cache readers");
                    // Once coordination is uncertain, stop serving affected
                    // caches. A fresh playback request revalidates the manifest.
                    foreach (var key in _readerLeases.Keys)
                        if (_jobs.TryGetValue(key, out var job))
                            MarkFailed(job, "The shared cache read lease could not be renewed. Retry playback.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await RefreshReaderLeasesAsync(releaseAll: true, deadline.Token); }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Shared transcoding cache reader leases will expire after shutdown");
            }
        }
    }

    private async Task RefreshReaderLeasesAsync(bool releaseAll, CancellationToken cancellationToken)
    {
        await _creationGate.WaitAsync(cancellationToken);
        try
        {
            CleanupExpiredSessions();
            if (_readerLeases.IsEmpty) return;
            await using var scope = _scopeFactory.CreateAsyncScope();
            var capacity = scope.ServiceProvider.GetService<IDownloadCapacityRepository>();
            await using var transaction = capacity is null ? null : await capacity.BeginAsync(cancellationToken);
            var repository = scope.ServiceProvider.GetService<ITranscodeCapacityRepository>();
            foreach (var pair in _readerLeases)
            {
                var lease = pair.Value;
                var inUse = !releaseAll && _jobs.TryGetValue(pair.Key, out var job)
                    && job.GetState() == TranscodingJobState.Ready && job.SessionCount > 0;
                if (!inUse)
                {
                    if (lease.Id != Guid.Empty)
                        await repository!.RemoveReaderAsync(lease.Id, cancellationToken);
                    _readerLeases.TryRemove(pair.Key, out _);
                    continue;
                }
                if (lease.Id == Guid.Empty) continue;
                var validUntil = Stopwatch.GetTimestamp() + TranscodeCapacityService.LeaseSeconds * Stopwatch.Frequency;
                if (!lease.IsValid || !await repository!.RenewReaderAsync(
                        lease.Id, TranscodeCapacityService.LeaseSeconds, cancellationToken))
                {
                    if (_jobs.TryGetValue(pair.Key, out var expired))
                        MarkFailed(expired, "The shared cache read lease expired. Retry playback.");
                    await repository!.RemoveReaderAsync(lease.Id, cancellationToken);
                    _readerLeases.TryRemove(pair.Key, out _);
                    continue;
                }
                _readerLeases[pair.Key] = lease with { ValidUntil = validUntil };
            }
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        finally { _creationGate.Release(); }
    }
}
