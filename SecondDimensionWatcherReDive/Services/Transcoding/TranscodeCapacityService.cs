using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal sealed class TranscodeCapacityService(
    IDownloadCapacityRepository downloads,
    ITranscodeCapacityRepository reservations,
    IConfiguration configuration,
    IOptions<TranscodingOptions> options) : ITranscodeCapacityBudget
{
    internal const int LeaseSeconds = 90;

    // Called inside the download admission transaction; never begin another budget transaction here.
    public async Task<long> GetRemainingBytesAsync(CancellationToken cancellationToken)
    {
        var rows = await reservations.ListActiveAsync(cancellationToken);
        return Saturate(rows.Where(row => row.CountsAgainstDownloads)
            .Sum(row => (decimal)Math.Max(0, row.BudgetBytes - row.WrittenBytes)));
    }

    public async Task<Guid?> TryAcquireAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var budget = options.Value.MaxDiskBytesPerJob;
        if (budget <= 0)
            throw new InvalidOperationException("Transcoding:MaxDiskBytesPerJob must be positive for capacity reservation.");
        var path = CapacityVolume.CanonicalPath(directoryPath);
        var cacheRoot = CapacityVolume.CanonicalPath(options.Value.CachePath);
        var cacheVolume = CapacityVolume.Identity(cacheRoot);
        var downloadPath = configuration["DownloadCapacity:LocalVolumePath"];
        var downloadVolume = string.IsNullOrWhiteSpace(downloadPath) ? null : CapacityVolume.Identity(downloadPath);
        var sharesDownloads = CapacityVolume.MayShare(cacheVolume, downloadVolume);
        var safety = Math.Max(0, configuration.GetValue<long?>("DownloadCapacity:SafetyBytes")
                                ?? 5L * 1024 * 1024 * 1024);

        await using var transaction = await downloads.BeginAsync(cancellationToken);
        await reservations.PruneExpiredAsync(cancellationToken);
        if (sharesDownloads) await downloads.RecoverTrackedAsync(cancellationToken);
        var active = await reservations.ListActiveAsync(cancellationToken);
        if (active.Any(row => row.DirectoryPath == path)) return null;
        var downloadRows = sharesDownloads ? await downloads.ListAsync(cancellationToken) : [];
        // Owner-published written bytes can lag, which retains extra reservation safely.
        // Across hosts filesystem device IDs are not comparable, so include all other HLS jobs conservatively.
        var remaining = active.Sum(row => (decimal)Math.Max(0, row.BudgetBytes - row.WrittenBytes))
                        + downloadRows.Where(row => row.State is "Reserved" or "Submitted")
                            .Sum(row => (decimal)Math.Max(0, row.RemainingBytes));
        var drive = DownloadCapacityService.FindDrive(cacheRoot)
                    ?? throw new IOException("The transcoding cache volume could not be located.");
        var available = drive.AvailableFreeSpace;
        if ((decimal)available - safety - remaining < budget)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var id = Guid.NewGuid();
        await reservations.AddAsync(id, path, cacheVolume, sharesDownloads, budget, LeaseSeconds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    public async Task<bool> RenewAsync(Guid id, string directoryPath, bool resetWritten,
        CancellationToken cancellationToken)
    {
        // Liveness never waits for remote I/O performed under the global budget lock.
        if (!await ExtendLeaseAsync(id, cancellationToken)) return false;
        if (resetWritten)
        {
            // Deleting generated files must first restore the full reservation.
            // The independent heartbeat keeps this lease alive while the lock is busy.
            await using var transaction = await downloads.BeginAsync(cancellationToken);
            var renewed = await reservations.RenewAsync(id, 0, LeaseSeconds, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return renewed;
        }

        using var publicationDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        publicationDeadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await using var transaction = await downloads.BeginAsync(publicationDeadline.Token);
            var written = GetWrittenBytes(directoryPath);
            var renewed = await reservations.RenewAsync(id, written, LeaseSeconds, publicationDeadline.Token);
            await transaction.CommitAsync(publicationDeadline.Token);
            return renewed;
        }
        catch (OperationCanceledException) when (publicationDeadline.IsCancellationRequested
                                                  && !cancellationToken.IsCancellationRequested)
        {
            // The earlier lease extension committed independently. Old written bytes
            // retain extra capacity, so skipping this publication cannot over-admit.
            return true;
        }
    }

    public Task<bool> ExtendLeaseAsync(Guid id, CancellationToken cancellationToken) =>
        reservations.ExtendLeaseAsync(id, LeaseSeconds, cancellationToken);

    public async Task ReleaseAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await downloads.BeginAsync(cancellationToken);
        await reservations.RemoveAsync(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static long GetWrittenBytes(string path)
    {
        if (!Directory.Exists(path)) return 0;
        decimal total = 0;
        // Completed or growing transport segments are monotonic until explicit hardware fallback.
        // Playlist/subtitle temporary files are intentionally left within the remaining reservation.
        foreach (var file in Directory.EnumerateFiles(path, "segment-*.ts", SearchOption.TopDirectoryOnly))
        {
            try { total += new FileInfo(file).Length; }
            catch (FileNotFoundException) { }
        }
        return Saturate(total);
    }

    private static long Saturate(decimal value) => (long)Math.Min(long.MaxValue, Math.Max(0, value));
}

internal sealed class TranscodeCapacityLease : IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Guid _id;
    private readonly string _directory;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _lost = new();
    private readonly SemaphoreSlim _renewalGate = new(1, 1);
    private readonly Task _heartbeat;
    private readonly ILogger _logger;

    public TranscodeCapacityLease(IServiceScopeFactory scopeFactory, Guid id, string directory, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _id = id;
        _directory = directory;
        _logger = logger;
        _heartbeat = RunHeartbeatAsync();
    }

    public CancellationToken LostToken => _lost.Token;

    public async Task ResetWrittenAsync(Action deleteFiles, CancellationToken cancellationToken)
    {
        await _renewalGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<TranscodeCapacityService>()
                    .RenewAsync(_id, _directory, true, cancellationToken))
            {
                await _lost.CancelAsync();
                throw new IOException("The transcoding capacity lease was lost.");
            }
            deleteFiles();
        }
        finally { _renewalGate.Release(); }
    }

    private async Task RunHeartbeatAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                await using var scope = _scopeFactory.CreateAsyncScope();
                var capacity = scope.ServiceProvider.GetRequiredService<TranscodeCapacityService>();
                if (!await _renewalGate.WaitAsync(0, deadline.Token))
                {
                    // Hardware fallback holds this local gate through the full-budget
                    // reset and deletion. Keep the lease alive without publishing bytes
                    // from files that are about to be deleted.
                    if (!await capacity.ExtendLeaseAsync(_id, deadline.Token))
                        throw new IOException("The transcoding capacity lease expired.");
                    continue;
                }
                try
                {
                    if (!await capacity.RenewAsync(_id, _directory, false, deadline.Token))
                        throw new IOException("The transcoding capacity lease expired.");
                }
                finally { _renewalGate.Release(); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Transcoding capacity lease {ReservationId} was lost", _id);
            await _lost.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _heartbeat;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TranscodeCapacityService>().ReleaseAsync(_id, deadline.Token);
        }
        catch (Exception exception)
        {
            // A crashed or unreachable owner retains its reservation until the bounded lease expires.
            _logger.LogWarning(exception, "Could not release transcoding capacity reservation {ReservationId}", _id);
        }
        _stop.Dispose();
        _lost.Dispose();
        _renewalGate.Dispose();
    }
}
