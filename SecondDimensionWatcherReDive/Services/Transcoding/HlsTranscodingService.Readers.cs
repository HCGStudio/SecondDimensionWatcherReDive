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
    private readonly object _readerStateGate = new();

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
        lock (_readerStateGate) _readerLeases[key] = new CacheReadLease(id, validUntil);
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
                    // A transient timeout does not invalidate a lease that is
                    // still within its last confirmed local lifetime.
                    foreach (var pair in _readerLeases)
                        if (!pair.Value.IsValid) InvalidateReaderLease(pair);
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
        CleanupExpiredSessions();
        if (!releaseAll)
        {
            // Neither the global budget lock nor _creationGate may delay
            // liveness: both can be held while a downloader request is slow.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetService<ITranscodeCapacityRepository>();
            foreach (var pair in _readerLeases)
            {
                var lease = pair.Value;
                if (lease.Id == Guid.Empty || !_jobs.TryGetValue(pair.Key, out var job)
                    || job.GetState() != TranscodingJobState.Ready || job.SessionCount == 0)
                    continue;
                if (!lease.IsValid)
                {
                    InvalidateReaderLease(pair);
                    continue;
                }
                var validUntil = Stopwatch.GetTimestamp() + TranscodeCapacityService.LeaseSeconds * Stopwatch.Frequency;
                // The conditional UPDATE cannot revive a pruned/expired lease.
                // A renewal before eviction extends protection; one after
                // expiry/deletion fails and the reader stops serving content.
                if (!await repository!.RenewReaderAsync(lease.Id, TranscodeCapacityService.LeaseSeconds, cancellationToken))
                    InvalidateReaderLease(pair);
                else
                    _readerLeases.TryUpdate(pair.Key, lease with { ValidUntil = validUntil }, lease);
            }
        }

        // Registration and removal remain serialized. Skip an occupied local
        // gate during normal sweeps so it cannot block the next heartbeat.
        if (releaseAll)
            await _creationGate.WaitAsync(cancellationToken);
        else if (!await _creationGate.WaitAsync(0, cancellationToken))
            return;
        try
        {
            if (_readerLeases.IsEmpty) return;
            using var cleanupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cleanupDeadline.CancelAfter(TimeSpan.FromSeconds(2));
            await using var scope = _scopeFactory.CreateAsyncScope();
            var capacity = scope.ServiceProvider.GetService<IDownloadCapacityRepository>();
            await using var transaction = capacity is null ? null : await capacity.BeginAsync(cleanupDeadline.Token);
            var repository = scope.ServiceProvider.GetService<ITranscodeCapacityRepository>();
            foreach (var pair in _readerLeases)
            {
                var inUse = !releaseAll && _jobs.TryGetValue(pair.Key, out var job)
                    && job.GetState() == TranscodingJobState.Ready && job.SessionCount > 0;
                if (inUse) continue;
                if (pair.Value.Id != Guid.Empty)
                    await repository!.RemoveReaderAsync(pair.Value.Id, cleanupDeadline.Token);
                _readerLeases.TryRemove(pair);
            }
            if (transaction is not null) await transaction.CommitAsync(cleanupDeadline.Token);
        }
        finally { _creationGate.Release(); }
    }

    private void InvalidateReaderLease(KeyValuePair<string, CacheReadLease> expected)
    {
        // Registration may have just installed a newer lease for this key.
        // Only invalidate the exact generation whose renewal failed.
        lock (_readerStateGate)
        {
            if (_readerLeases.TryGetValue(expected.Key, out var current) && ReferenceEquals(current, expected.Value)
                && _jobs.TryGetValue(expected.Key, out var job))
                MarkFailed(job, "The shared cache read lease expired. Retry playback.");
        }
    }
}
