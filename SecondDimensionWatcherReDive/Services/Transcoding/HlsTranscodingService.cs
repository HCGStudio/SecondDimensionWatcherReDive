using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal sealed partial class HlsTranscodingService : BackgroundService, IHlsTranscodingService
{
    private const int CacheManifestVersion = 2;
    private const string CacheOwnershipMarker = ".sdw-transcode-cache";
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFfmpegProcessRunner _processRunner;
    private readonly TranscodingMetrics _metrics;
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly ILogger<HlsTranscodingService> _logger;
    private readonly TranscodingOptions _options;
    private readonly Channel<TranscodingJob> _queue;
    private readonly ConcurrentDictionary<string, TranscodingJob> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, TranscodingSession> _sessions = new();
    private readonly ConcurrentDictionary<TranscodingJob, long> _deferredJobs = new();
    private readonly SemaphoreSlim _creationGate = new(1, 1);
    private long _queueOrdinal;

    public HlsTranscodingService(
        IServiceScopeFactory scopeFactory,
        IFfmpegProcessRunner processRunner,
        TranscodingMetrics metrics,
        IContentTypeProvider contentTypeProvider,
        IOptions<TranscodingOptions> options,
        ILogger<HlsTranscodingService> logger)
    {
        _scopeFactory = scopeFactory;
        _processRunner = processRunner;
        _metrics = metrics;
        _contentTypeProvider = contentTypeProvider;
        _logger = logger;
        _options = options.Value;
        _queue = Channel.CreateBounded<TranscodingJob>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = _options.MaxConcurrentJobs == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public async Task<TranscodingSessionStatus> PrepareAsync(
        Guid animationInfoId,
        string? relativePath,
        TranscodingSelection selection,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled) throw new TranscodingDisabledException();
        var source = await ResolveSourceAsync(animationInfoId, relativePath, cancellationToken);
        var cacheKey = BuildCacheKey(source, selection);

        await _creationGate.WaitAsync(cancellationToken);
        try
        {
            if (_jobs.TryGetValue(cacheKey, out var terminalJob)
                && terminalJob.GetState() is TranscodingJobState.Failed or TranscodingJobState.Canceled)
                _jobs.TryRemove(new KeyValuePair<string, TranscodingJob>(cacheKey, terminalJob));

            if (_jobs.TryGetValue(cacheKey, out var readyJob)
                && readyJob.GetState() == TranscodingJobState.Ready
                && readyJob.GetStrategy() != TranscodingStrategy.Direct
                && await TryLoadProtectedManifestAsync(readyJob.CacheDirectory, cancellationToken) is null)
                MarkFailed(readyJob, "The shared cache is no longer available. Retry playback.");

            var isNewJob = false;
            var cacheHit = false;
            if (!_jobs.TryGetValue(cacheKey, out var job))
            {
                var cacheDirectory = Path.Combine(_options.CachePath, cacheKey);
                var manifest = await TryLoadProtectedManifestAsync(cacheDirectory, cancellationToken);
                if (manifest is not null)
                {
                    job = TranscodingJob.FromManifest(
                        cacheKey,
                        cacheDirectory,
                        source,
                        selection,
                        manifest);
                    cacheHit = true;
                    _metrics.RecordCacheHit();
                }
                else
                {
                    // Deferred capacity waiters remain part of the bounded
                    // outstanding work even while they occupy no worker slot.
                    if (_jobs.Values.Count(candidate => candidate.GetState() is TranscodingJobState.Queued
                            or TranscodingJobState.Probing or TranscodingJobState.Transcoding)
                        >= _options.QueueCapacity + _options.MaxConcurrentJobs)
                        throw new TranscodingQueueFullException();
                    job = new TranscodingJob(
                        cacheKey,
                        cacheDirectory,
                        source,
                        selection,
                        Interlocked.Increment(ref _queueOrdinal));
                    isNewJob = true;
                }
                _jobs[cacheKey] = job;
            }
            else
            {
                cacheHit = job.GetState() == TranscodingJobState.Ready;
                if (cacheHit) _metrics.RecordCacheHit();
            }

            var session = new TranscodingSession(job, cacheHit, _options.SessionTtl);
            _sessions[session.Id] = session;
            job.AddSession(session.Id);

            if (isNewJob && !_queue.Writer.TryWrite(job))
            {
                _sessions.TryRemove(session.Id, out _);
                job.RemoveSession(session.Id);
                _jobs.TryRemove(new KeyValuePair<string, TranscodingJob>(cacheKey, job));
                job.Cancellation.Dispose();
                throw new TranscodingQueueFullException();
            }

            UpdateJobGauges();
            TouchCache(job, session);
            return BuildStatus(session);
        }
        finally
        {
            _creationGate.Release();
        }
    }

    public Task<TranscodingSessionStatus?> GetStatusAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = FindSession(sessionId, accessToken);
        if (session is null) return Task.FromResult<TranscodingSessionStatus?>(null);
        TouchCache(session.Job, session);
        return Task.FromResult<TranscodingSessionStatus?>(BuildStatus(session));
    }

    public async Task<string?> GetPlaylistAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var session = FindSession(sessionId, accessToken);
        if (session is null || !session.Job.GetIsPlayable()) return null;
        TouchCache(session.Job, session);
        var playlistPath = Path.Combine(session.Job.CacheDirectory, "media.m3u8");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await File.ReadAllTextAsync(playlistPath, cancellationToken);
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(25, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
        return null;
    }

    public Task<TranscodingContent?> OpenSegmentAsync(
        Guid sessionId,
        string accessToken,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = FindSession(sessionId, accessToken);
        if (session is null || !session.Job.GetIsPlayable() || !IsSegmentName(fileName))
            return Task.FromResult<TranscodingContent?>(null);

        var path = Path.Combine(session.Job.CacheDirectory, fileName);
        if (!File.Exists(path)) return Task.FromResult<TranscodingContent?>(null);
        TouchCache(session.Job, session);
        return Task.FromResult<TranscodingContent?>(OpenCachedContent(path, "video/mp2t"));
    }

    public Task<TranscodingContent?> OpenSubtitleAsync(
        Guid sessionId,
        string accessToken,
        string fileName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = FindSession(sessionId, accessToken);
        if (session is null || session.Job.GetState() is TranscodingJobState.Failed or TranscodingJobState.Canceled
            || !session.Job.HasSubtitle(fileName))
            return Task.FromResult<TranscodingContent?>(null);

        var path = Path.Combine(session.Job.CacheDirectory, fileName);
        if (!File.Exists(path)) return Task.FromResult<TranscodingContent?>(null);
        TouchCache(session.Job, session);
        return Task.FromResult<TranscodingContent?>(OpenCachedContent(path, "text/vtt; charset=utf-8"));
    }

    public async Task<TranscodingContent?> OpenDirectAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var session = FindSession(sessionId, accessToken);
        if (session is null || session.Job.GetStrategy() != TranscodingStrategy.Direct) return null;
        session.Touch(_options.SessionTtl);
        var stream = await OpenSourceStreamAsync(session.Job.Source, cancellationToken);
        var contentType = _contentTypeProvider.TryGetContentType(session.Job.Source.FileName, out var type)
            ? type
            : "application/octet-stream";
        return new TranscodingContent(
            stream,
            contentType,
            session.Job.Source.FileName,
            session.Job.Source.Length,
            session.Job.Source.LastModifiedUtc);
    }

    public Task<bool> CancelAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = FindSession(sessionId, accessToken, touch: false);
        if (session is null || !_sessions.TryRemove(sessionId, out _)) return Task.FromResult(false);
        ReleaseSession(session);
        return Task.FromResult(true);
    }

    public Task<TranscodingMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_metrics.Snapshot());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_options.CachePath);
        await CleanupCacheAsync(removeIncomplete: true, stoppingToken);
        var workers = Enumerable.Range(0, _options.MaxConcurrentJobs)
            .Select(_ => RunWorkerAsync(stoppingToken))
            .ToArray();
        var cleanup = RunCleanupLoopAsync(stoppingToken);
        try
        {
            await Task.WhenAll(workers.Append(cleanup).Append(RunReaderLeaseLoopAsync(stoppingToken))
                .Append(RunDeferredJobsAsync(stoppingToken)));
        }
        finally
        {
            // All worker tasks have stopped before pending/deferred jobs are
            // released. No detached retry tasks survive the hosted service.
            _deferredJobs.Clear();
            while (_queue.Reader.TryRead(out _)) { }
            foreach (var job in _jobs.Values)
                if (job.GetState() is TranscodingJobState.Queued or TranscodingJobState.Probing or TranscodingJobState.Transcoding)
                    MarkCanceled(job);
        }
    }

    private async Task RunWorkerAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProcessJobAsync(TranscodingJob job, CancellationToken stoppingToken)
    {
        if (job.Cancellation.IsCancellationRequested)
        {
            MarkCanceled(job);
            return;
        }

        var remainingTime = job.GetRemainingTimeout(_options.JobTimeout);
        if (remainingTime <= TimeSpan.Zero)
        {
            MarkFailed(job, "The transcoding job exceeded its configured timeout.");
            return;
        }
        using var timeout = new CancellationTokenSource(remainingTime);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            job.Cancellation.Token,
            timeout.Token);
        var cancellationToken = linked.Token;
        var startedAt = DateTimeOffset.UtcNow - (_options.JobTimeout - remainingTime);
        TranscodeCapacityLease? capacityLease = null;
        var createdOutput = false;
        try
        {
            var prepared = job.GetPreparation();
            if (prepared is null)
            {
                job.SetState(TranscodingJobState.Probing);
                UpdateJobGauges();
                MediaProbe initialProbe;
                await using (var source = await OpenSourceStreamAsync(job.Source, cancellationToken))
                    initialProbe = await _processRunner.ProbeAsync(source, cancellationToken);
                var initialPlan = TranscodingPlanner.CreatePlan(
                    job.Source, initialProbe, job.Selection, _options.BurnBitmapSubtitles);
                job.SetPreparation(initialProbe, initialPlan);
                prepared = (initialProbe, initialPlan);
            }
            var (probe, plan) = prepared.Value;

            if (plan.Strategy == TranscodingStrategy.Direct)
            {
                job.MarkPlayable();
                _metrics.RecordFirstSegment(DateTimeOffset.UtcNow - startedAt);
                _metrics.RecordCompleted();
                job.SetReady([]);
                UpdateJobGauges();
                return;
            }

            var capacity = await TryAcquireCapacityAsync(job, cancellationToken);
            capacityLease = capacity.Lease;
            if (!capacity.Acquired)
            {
                job.SetWaitingForCapacity(_options.MaxDiskBytesPerJob);
                _deferredJobs[job] = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
                UpdateJobGauges();
                return;
            }
            if (job.GetState() == TranscodingJobState.Ready) return;
            using var capacityCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, capacityLease?.LostToken ?? CancellationToken.None);
            cancellationToken = capacityCancellation.Token;
            // Another replica can complete this cache while our job waits for
            // its reservation. Adopt it while the shared directory is protected.
            if (await TryReuseCompletedCacheAsync(job, cancellationToken)) return;
            RecreateJobDirectory(job.CacheDirectory);
            createdOutput = true;
            job.SetState(TranscodingJobState.Transcoding);
            UpdateJobGauges();
            using var subtitleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var subtitleTask = ExtractTextSubtitlesAsync(job, plan, subtitleCancellation.Token);
            var subtitleTaskObserved = false;
            var firstSegmentRecorded = 0;
            void OnProgress(FfmpegProgress update)
            {
                var fraction = FfmpegProcessRunner.ToProgressFraction(update.ProcessedSeconds, probe.Duration);
                job.SetProgress(fraction, update.Speed);
                if (update.FirstSegmentReady)
                {
                    job.MarkPlayable();
                    if (Interlocked.Exchange(ref firstSegmentRecorded, 1) == 0)
                        _metrics.RecordFirstSegment(DateTimeOffset.UtcNow - startedAt);
                }
            }

            try
            {
                var useHardware = !plan.CopyVideo && !string.IsNullOrWhiteSpace(_options.HardwareVideoEncoder);
                FfmpegRunResult result;
                await using (var source = await OpenSourceStreamAsync(job.Source, cancellationToken))
                    result = await _processRunner.GenerateHlsAsync(
                        source,
                        plan,
                        job.Selection,
                        job.CacheDirectory,
                        useHardware,
                        OnProgress,
                        cancellationToken);
                if (result.ExitCode != 0 && useHardware)
                {
                    LogHardwareFallback(_logger, _options.HardwareVideoEncoder!, result.ErrorOutput);
                    if (capacityLease is not null)
                        await capacityLease.ResetWrittenAsync(
                            () => DeleteGeneratedHlsFiles(job.CacheDirectory), cancellationToken);
                    else
                        DeleteGeneratedHlsFiles(job.CacheDirectory);
                    await using var source = await OpenSourceStreamAsync(job.Source, cancellationToken);
                    result = await _processRunner.GenerateHlsAsync(
                        source,
                        plan,
                        job.Selection,
                        job.CacheDirectory,
                        useHardwareEncoder: false,
                        OnProgress,
                        cancellationToken);
                }
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"FFmpeg exited with code {result.ExitCode}: {result.ErrorOutput}");

                var subtitles = await subtitleTask;
                subtitleTaskObserved = true;
                await WriteManifestAsync(job, cancellationToken);
                _metrics.RecordCompleted();
                if (job.GetSpeed() is { } speed) _metrics.RecordSpeed(speed);
                UpdateCacheBytes();
                await _creationGate.WaitAsync(cancellationToken);
                try
                {
                    if (await TryLoadProtectedManifestAsync(job.CacheDirectory, cancellationToken) is null)
                        throw new IOException("The completed transcoding cache is unavailable.");
                    job.SetReady(subtitles);
                }
                finally { _creationGate.Release(); }
                UpdateJobGauges();
                await CleanupCacheAsync(removeIncomplete: false, cancellationToken);
            }
            finally
            {
                if (!subtitleTaskObserved)
                {
                    try
                    {
                        await subtitleCancellation.CancelAsync();
                        await subtitleTask;
                    }
                    catch (OperationCanceledException) when (subtitleCancellation.IsCancellationRequested) { }
                    catch (Exception exception) { LogSubtitleCleanupFailed(_logger, exception); }
                }
            }
        }
        catch (OperationCanceledException) when (capacityLease?.LostToken.IsCancellationRequested == true)
        {
            MarkFailed(job, "The capacity reservation expired or could not be renewed. Retry playback after storage coordination recovers.");
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            await CleanupPartialOutputAsync();
            MarkCanceled(job);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await CleanupPartialOutputAsync();
            MarkFailed(job, "The transcoding job exceeded its configured timeout.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await CleanupPartialOutputAsync();
            MarkCanceled(job);
            throw;
        }
        catch (Exception exception)
        {
            LogJobFailed(_logger, job.Source.VirtualPath, exception);
            await CleanupPartialOutputAsync();
            MarkFailed(job, exception.Message);
        }
        finally
        {
            if (capacityLease is not null) await capacityLease.DisposeAsync();
        }

        async Task CleanupPartialOutputAsync()
        {
            // Waiting/probing jobs never own this directory. Once published,
            // a manifest may already serve readers on another replica.
            if (!createdOutput || File.Exists(Path.Combine(job.CacheDirectory, "complete.json"))) return;
            if (capacityLease is null)
                TryDeleteDirectory(job.CacheDirectory);
            else
                await capacityLease.CleanupIncompleteAsync(() =>
                {
                    if (!File.Exists(Path.Combine(job.CacheDirectory, "complete.json")))
                        TryDeleteDirectory(job.CacheDirectory);
                });
        }
    }

    private async Task<bool> TryReuseCompletedCacheAsync(TranscodingJob job, CancellationToken cancellationToken)
    {
        await _creationGate.WaitAsync(cancellationToken);
        try
        {
            var manifest = await TryLoadProtectedManifestAsync(job.CacheDirectory, cancellationToken);
            if (manifest is null) return false;
            job.SetReady(manifest.Subtitles);
            _metrics.RecordCacheHit();
            TouchCache(job, null);
            UpdateJobGauges();
            return true;
        }
        finally { _creationGate.Release(); }
    }

    private async Task<(bool Acquired, TranscodeCapacityLease? Lease)> TryAcquireCapacityAsync(
        TranscodingJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Completed manifests need no new write budget. Otherwise make one
        // admission attempt and let the deferred scheduler release this worker.
        if (await TryReuseCompletedCacheAsync(job, cancellationToken)) return (true, null);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var capacity = scope.ServiceProvider.GetService<TranscodeCapacityService>();
        if (capacity is null) return (true, null);
        var reservation = await capacity.TryAcquireAsync(job.CacheDirectory, cancellationToken);
        return reservation is { } id
            ? (true, new TranscodeCapacityLease(_scopeFactory, id, job.CacheDirectory, _logger))
            : (false, null);
    }

    private async Task RunDeferredJobsAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var pair in _deferredJobs.OrderBy(pair => pair.Value))
            {
                var job = pair.Key;
                if (job.Cancellation.IsCancellationRequested)
                {
                    if (_deferredJobs.TryRemove(pair)) MarkCanceled(job);
                }
                else if (job.GetRemainingTimeout(_options.JobTimeout) <= TimeSpan.Zero)
                {
                    if (_deferredJobs.TryRemove(pair))
                        MarkFailed(job, "The transcoding job exceeded its configured timeout.");
                }
                else if (pair.Value <= Stopwatch.GetTimestamp() && _queue.Writer.TryWrite(job))
                {
                    // A fast worker may already defer the job again. Remove
                    // only the due generation that was actually enqueued.
                    _deferredJobs.TryRemove(pair);
                }
            }
        }
    }

    private async Task RunCleanupLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupCacheAsync(removeIncomplete: true, stoppingToken);
    }

    private async Task<TranscodingSource> ResolveSourceAsync(
        Guid animationInfoId,
        string? relativePath,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var animationRepository = scope.ServiceProvider.GetRequiredService<IAnimationInfoRepository>();
        var info = await animationRepository.FindByIdWithAnimationAsync(animationInfoId, cancellationToken);
        if (info is null || !info.IsDownloadFinished)
            throw new KeyNotFoundException("The requested animation is not available for playback.");

        var virtualPath = PlaybackPathResolver.ResolveVirtualPath(info, relativePath);
        var mapping = await scope.ServiceProvider.GetRequiredService<IFileMappingRepository>()
            .FindByVirtualPathAsync(virtualPath, cancellationToken);
        if (mapping is null || mapping.AnimationInfoId != animationInfoId)
            throw new KeyNotFoundException("The requested playback file mapping was not found.");

        var store = scope.ServiceProvider.GetRequiredService<IFileStoreProvider>()
            .GetRequiredClient(mapping.FileStore);
        var fileInfo = await store.FileInfoAsync(mapping.PhysicalPath, cancellationToken);
        if (fileInfo.IsDirectory)
            throw new KeyNotFoundException("The requested playback path is a directory.");
        return new TranscodingSource(
            animationInfoId,
            mapping.Id,
            mapping.VirtualPath,
            mapping.PhysicalPath,
            mapping.FileStore,
            fileInfo.FileName,
            fileInfo.Length ?? 0,
            fileInfo.LastModifiedUtc ?? DateTimeOffset.UnixEpoch);
    }

    private async Task<Stream> OpenSourceStreamAsync(
        TranscodingSource source,
        CancellationToken cancellationToken)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<IFileStoreProvider>()
                .GetRequiredClient(source.FileStore);
            var stream = await store.OpenReadStreamAsync(source.PhysicalPath, cancellationToken);
            return new ScopeOwnedStream(stream, scope);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private async Task<IReadOnlyList<TranscodingSubtitle>> ExtractTextSubtitlesAsync(
        TranscodingJob job,
        TranscodingPlan plan,
        CancellationToken cancellationToken)
    {
        var subtitles = new List<TranscodingSubtitle>();
        for (var index = 0; index < plan.TextSubtitles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var track = plan.TextSubtitles[index];
            TranscodingSubtitle? subtitle;
            await using (var source = await OpenSourceStreamAsync(job.Source, cancellationToken))
                subtitle = await _processRunner.ExtractTextSubtitleAsync(
                    source,
                    track,
                    index + 1,
                    job.CacheDirectory,
                    cancellationToken);
            if (subtitle is null) continue;

            subtitles.Add(subtitle);
            job.SetSubtitles(subtitles.ToArray());
        }
        return subtitles;
    }

    private TranscodingSession? FindSession(Guid id, string token, bool touch = true)
    {
        if (!_sessions.TryGetValue(id, out var session) || !TokensEqual(session.AccessToken, token))
            return null;
        if (session.IsExpired)
        {
            if (_sessions.TryRemove(id, out _)) ReleaseSession(session);
            return null;
        }
        if (session.Job.GetState() == TranscodingJobState.Ready
            && session.Job.GetStrategy() != TranscodingStrategy.Direct
            && (!_readerLeases.TryGetValue(session.Job.CacheKey, out var readLease) || !readLease.IsValid))
            MarkFailed(session.Job, "The shared cache read lease expired. Retry playback.");
        if (touch) session.Touch(_options.SessionTtl);
        return session;
    }

    private TranscodingSessionStatus BuildStatus(TranscodingSession session)
    {
        var job = session.Job;
        var state = job.GetState();
        int? queuePosition = state == TranscodingJobState.Queued
            ? _jobs.Values.Count(candidate =>
                candidate.GetState() == TranscodingJobState.Queued
                && candidate.QueueOrdinal <= job.QueueOrdinal)
            : null;
        return job.CreateStatus(session.Id, session.AccessToken, session.CacheHit, queuePosition);
    }

    private async Task<CacheManifest?> TryLoadManifestAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "complete.json");
        if (!File.Exists(Path.Combine(directory, CacheOwnershipMarker))
            || !File.Exists(path)
            || !File.Exists(Path.Combine(directory, "media.m3u8"))) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<CacheManifest>(stream, cancellationToken: cancellationToken);
            return manifest?.Version == CacheManifestVersion ? manifest : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            LogInvalidCacheManifest(_logger, path, exception);
            // Reads also happen before acquiring directory ownership. Any
            // recreation must wait for the writer's cross-replica reservation.
            return null;
        }
    }

    private async Task WriteManifestAsync(TranscodingJob job, CancellationToken cancellationToken)
    {
        var manifest = job.CreateManifest();
        var path = Path.Combine(job.CacheDirectory, "complete.json");
        var temporaryPath = $"{path}.tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
            await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        TouchCache(job, null);
    }

    private async Task CleanupCacheAsync(bool removeIncomplete, CancellationToken cancellationToken)
    {
        await _creationGate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var capacity = scope.ServiceProvider.GetService<IDownloadCapacityRepository>();
            if (capacity is null)
            {
                CleanupCacheCore(removeIncomplete, new HashSet<string>(StringComparer.Ordinal), cancellationToken);
            }
            else
            {
                // Preserve work owned by another replica, including during startup cleanup.
                await using var transaction = await capacity.BeginAsync(cancellationToken);
                var cacheRepository = scope.ServiceProvider.GetRequiredService<ITranscodeCapacityRepository>();
                await cacheRepository.PruneExpiredAsync(cancellationToken);
                var reservations = await cacheRepository.ListActiveAsync(cancellationToken);
                var readers = await cacheRepository.ListActiveReadersAsync(cancellationToken);
                var protectedKeys = reservations.Select(row => Path.GetFileName(row.DirectoryPath))
                    .Concat(readers.Select(row => Path.GetFileName(row.DirectoryPath)))
                    .ToHashSet(StringComparer.Ordinal);
                CleanupCacheCore(removeIncomplete, protectedKeys, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            _creationGate.Release();
        }
    }

    private void CleanupCacheCore(bool removeIncomplete, IReadOnlySet<string> protectedKeys, CancellationToken cancellationToken)
    {
        CleanupExpiredSessions();
        if (!Directory.Exists(_options.CachePath)) return;
        var now = DateTimeOffset.UtcNow;
        var candidates = new List<CacheDirectory>();
        foreach (var directory in Directory.EnumerateDirectories(_options.CachePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Path.GetFileName(directory);
            if (!IsCacheKey(key)) continue;
            if (!File.Exists(Path.Combine(directory, CacheOwnershipMarker))) continue;
            var completePath = Path.Combine(directory, "complete.json");
            if (!File.Exists(completePath))
            {
                if (removeIncomplete && !protectedKeys.Contains(key.ToUpperInvariant()) && !IsActive(key)) TryDeleteDirectory(directory);
                continue;
            }
            var accessPath = Path.Combine(directory, ".access");
            var lastAccess = File.Exists(accessPath)
                ? File.GetLastWriteTimeUtc(accessPath)
                : File.GetLastWriteTimeUtc(completePath);
            candidates.Add(new CacheDirectory(key, directory, lastAccess, GetDirectorySize(directory)));
        }

        foreach (var expired in candidates
                     .Where(candidate => now - candidate.LastAccess > _options.CacheTtl)
                     .OrderBy(candidate => candidate.LastAccess)
                     .ToArray())
        {
            if (protectedKeys.Contains(expired.Key.ToUpperInvariant()) || IsInUse(expired.Key)) continue;
            RemoveCacheDirectory(expired);
            candidates.Remove(expired);
        }

        var total = candidates.Sum(candidate => candidate.Size);
        foreach (var candidate in candidates.OrderBy(candidate => candidate.LastAccess))
        {
            if (total <= _options.MaxCacheBytes) break;
            if (protectedKeys.Contains(candidate.Key.ToUpperInvariant()) || IsInUse(candidate.Key)) continue;
            RemoveCacheDirectory(candidate);
            total -= candidate.Size;
        }
        UpdateCacheBytes();
    }

    private void CleanupExpiredSessions()
    {
        foreach (var pair in _sessions)
            if (pair.Value.IsExpired && _sessions.TryRemove(pair.Key, out var session)) ReleaseSession(session);
    }

    private void ReleaseSession(TranscodingSession session)
    {
        var remaining = session.Job.RemoveSession(session.Id);
        if (remaining == 0
            && session.Job.GetState() is TranscodingJobState.Queued
                or TranscodingJobState.Probing
                or TranscodingJobState.Transcoding)
            session.Job.Cancellation.Cancel();
        else if (remaining == 0
                 && session.Job.GetState() == TranscodingJobState.Ready
                 && session.Job.GetStrategy() == TranscodingStrategy.Direct
                 && _jobs.TryRemove(new KeyValuePair<string, TranscodingJob>(
                     session.Job.CacheKey,
                     session.Job)))
            session.Job.Cancellation.Dispose();
    }

    private bool IsActive(string key)
        => _jobs.TryGetValue(key, out var job)
           && job.GetState() is TranscodingJobState.Queued
               or TranscodingJobState.Probing
               or TranscodingJobState.Transcoding;

    private bool IsInUse(string key)
        => _jobs.TryGetValue(key, out var job) && (job.SessionCount > 0 || IsActive(key));

    private void RemoveCacheDirectory(CacheDirectory candidate)
    {
        TryDeleteDirectory(candidate.Path);
        if (_jobs.TryGetValue(candidate.Key, out var job) && job.GetState() == TranscodingJobState.Ready)
            _jobs.TryRemove(new KeyValuePair<string, TranscodingJob>(candidate.Key, job));
    }

    private void MarkCanceled(TranscodingJob job)
    {
        job.SetCanceled();
        _jobs.TryRemove(new KeyValuePair<string, TranscodingJob>(job.CacheKey, job));
        _metrics.RecordCanceled();
        UpdateCacheBytes();
        UpdateJobGauges();
    }

    private void MarkFailed(TranscodingJob job, string error)
    {
        job.SetFailed(error);
        _jobs.TryRemove(new KeyValuePair<string, TranscodingJob>(job.CacheKey, job));
        _metrics.RecordFailed();
        UpdateCacheBytes();
        UpdateJobGauges();
    }

    private void UpdateJobGauges()
    {
        _metrics.SetQueued(_jobs.Values.Count(job => job.GetState() == TranscodingJobState.Queued));
        _metrics.SetActive(_jobs.Values.Count(job =>
            job.GetState() is TranscodingJobState.Probing or TranscodingJobState.Transcoding));
    }

    private void UpdateCacheBytes() => _metrics.SetCacheBytes(GetDirectorySize(_options.CachePath));

    private void TouchCache(TranscodingJob job, TranscodingSession? session)
    {
        session?.Touch(_options.SessionTtl);
        if (!Directory.Exists(job.CacheDirectory)) return;
        if (session is not null && !session.ShouldTouchCache) return;
        try
        {
            var marker = Path.Combine(job.CacheDirectory, ".access");
            if (!File.Exists(marker)) File.WriteAllText(marker, string.Empty);
            File.SetLastWriteTimeUtc(marker, DateTime.UtcNow);
            session?.MarkCacheTouched();
        }
        catch (IOException) { }
    }

    private string BuildCacheKey(TranscodingSource source, TranscodingSelection selection)
    {
        var material = string.Join('|',
            source.BuildCacheKey(selection),
            CacheManifestVersion,
            _options.SegmentDurationSeconds,
            _options.VideoCrf,
            _options.VideoPreset,
            _options.HardwareVideoEncoder ?? string.Empty,
            string.Join('\u001f', _options.HardwareInputArguments),
            _options.BurnBitmapSubtitles);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static TranscodingContent OpenCachedContent(string path, string contentType)
    {
        var info = new FileInfo(path);
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new TranscodingContent(
            stream,
            contentType,
            info.Name,
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static void RecreateJobDirectory(string path)
    {
        if (Directory.Exists(path) && !File.Exists(Path.Combine(path, CacheOwnershipMarker)))
            throw new InvalidOperationException(
                $"The transcoding cache path '{path}' is occupied by an unmanaged directory.");
        TryDeleteDirectory(path);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, CacheOwnershipMarker), string.Empty);
    }

    private static void DeleteGeneratedHlsFiles(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var fileName = Path.GetFileName(path);
            if (fileName is "media.m3u8" or "media.m3u8.tmp"
                || fileName.StartsWith("segment-", StringComparison.Ordinal))
                try { File.Delete(path); }
                catch (IOException) { }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && File.Exists(Path.Combine(path, CacheOwnershipMarker)))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch (IOException) { return 0; }
                });
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static bool TokensEqual(string expected, string actual)
    {
        if (expected.Length != actual.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }

    private static bool IsSegmentName(string name)
        => name == Path.GetFileName(name)
           && name.StartsWith("segment-", StringComparison.Ordinal)
           && name.EndsWith(".ts", StringComparison.Ordinal)
           && name[8..^3].All(char.IsAsciiDigit);

    private static bool IsCacheKey(string name)
        => name.Length == 64 && name.All(char.IsAsciiHexDigit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hardware encoder {Encoder} failed; retrying with the CPU encoder. FFmpeg: {Error}")]
    private static partial void LogHardwareFallback(ILogger logger, string encoder, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transcoding failed for {VirtualPath}")]
    private static partial void LogJobFailed(ILogger logger, string virtualPath, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid transcoding cache manifest {Path}")]
    private static partial void LogInvalidCacheManifest(ILogger logger, string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed while stopping background subtitle extraction")]
    private static partial void LogSubtitleCleanupFailed(ILogger logger, Exception exception);

    private sealed record CacheDirectory(string Key, string Path, DateTimeOffset LastAccess, long Size);

    private sealed record CacheManifest(
        int Version,
        TranscodingStrategy Strategy,
        string VideoCodec,
        string? AudioCodec,
        TranscodingSubtitle[] Subtitles,
        int UnsupportedSubtitleCount);

    private sealed class TranscodingSession
    {
        private long _expiresAtTicks;
        private long _lastCacheTouchTicks;

        public TranscodingSession(TranscodingJob job, bool cacheHit, TimeSpan ttl)
        {
            Job = job;
            CacheHit = cacheHit;
            Id = Guid.NewGuid();
            AccessToken = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            Touch(ttl);
        }

        public Guid Id { get; }
        public string AccessToken { get; }
        public TranscodingJob Job { get; }
        public bool CacheHit { get; }
        public bool IsExpired => DateTimeOffset.UtcNow.UtcTicks > Interlocked.Read(ref _expiresAtTicks);
        public bool ShouldTouchCache
            => DateTimeOffset.UtcNow.UtcTicks - Interlocked.Read(ref _lastCacheTouchTicks) > TimeSpan.FromMinutes(1).Ticks;

        public void Touch(TimeSpan ttl)
            => Interlocked.Exchange(ref _expiresAtTicks, (DateTimeOffset.UtcNow + ttl).UtcTicks);

        public void MarkCacheTouched()
            => Interlocked.Exchange(ref _lastCacheTouchTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    private sealed class TranscodingJob
    {
        private readonly object _gate = new();
        private readonly HashSet<Guid> _sessions = [];
        private TranscodingJobState _state = TranscodingJobState.Queued;
        private TranscodingPlan? _plan;
        private MediaProbe? _probe;
        private long _startedTimestamp;
        private bool _isPlayable;
        private double? _progress;
        private double? _speed;
        private string? _error;
        private IReadOnlyList<TranscodingSubtitle> _subtitles = [];

        public TranscodingJob(
            string cacheKey,
            string cacheDirectory,
            TranscodingSource source,
            TranscodingSelection selection,
            long queueOrdinal)
        {
            CacheKey = cacheKey;
            CacheDirectory = cacheDirectory;
            Source = source;
            Selection = selection;
            QueueOrdinal = queueOrdinal;
        }

        public string CacheKey { get; }
        public string CacheDirectory { get; }
        public TranscodingSource Source { get; }
        public TranscodingSelection Selection { get; }
        public long QueueOrdinal { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public int SessionCount { get { lock (_gate) return _sessions.Count; } }

        public static TranscodingJob FromManifest(
            string cacheKey,
            string cacheDirectory,
            TranscodingSource source,
            TranscodingSelection selection,
            CacheManifest manifest)
        {
            var job = new TranscodingJob(cacheKey, cacheDirectory, source, selection, 0)
            {
                _state = TranscodingJobState.Ready,
                _isPlayable = true,
                _progress = 1,
                _subtitles = manifest.Subtitles
            };
            var video = new MediaStreamProbe(
                0,
                "video",
                manifest.VideoCodec,
                null,
                null,
                true,
                false,
                false,
                null,
                null);
            var audio = manifest.AudioCodec is null
                ? null
                : new MediaStreamProbe(
                    1,
                    "audio",
                    manifest.AudioCodec,
                    null,
                    null,
                    true,
                    false,
                    false,
                    null,
                    null);
            job._plan = new TranscodingPlan(
                manifest.Strategy,
                video,
                audio,
                null,
                [],
                manifest.UnsupportedSubtitleCount,
                manifest.Strategy == TranscodingStrategy.Remux,
                manifest.Strategy == TranscodingStrategy.Remux);
            return job;
        }

        public void AddSession(Guid id) { lock (_gate) _sessions.Add(id); }
        public int RemoveSession(Guid id) { lock (_gate) { _sessions.Remove(id); return _sessions.Count; } }
        public TranscodingJobState GetState() { lock (_gate) return _state; }
        public TranscodingStrategy? GetStrategy() { lock (_gate) return _plan?.Strategy; }
        public bool GetIsPlayable() { lock (_gate) return _isPlayable; }
        public double? GetSpeed() { lock (_gate) return _speed; }
        public bool HasSubtitle(string fileName)
        {
            lock (_gate) return fileName == Path.GetFileName(fileName) && _subtitles.Any(item => item.FileName == fileName);
        }

        public void SetState(TranscodingJobState state) { lock (_gate) { _state = state; _error = null; } }
        public void SetWaitingForCapacity(long requiredBytes)
        {
            lock (_gate)
            {
                _state = TranscodingJobState.Queued;
                _error = $"Waiting for {requiredBytes} bytes of transcoding capacity after the safety reserve and active download/transcoding reservations. Playback resumes automatically when space is available.";
            }
        }
        public (MediaProbe Probe, TranscodingPlan Plan)? GetPreparation()
        {
            lock (_gate) return _probe is not null && _plan is not null ? (_probe, _plan) : null;
        }
        public void SetPreparation(MediaProbe probe, TranscodingPlan plan)
        {
            lock (_gate) { _probe = probe; _plan = plan; }
        }
        public TimeSpan GetRemainingTimeout(TimeSpan maximum)
        {
            var now = Stopwatch.GetTimestamp();
            var started = Interlocked.CompareExchange(ref _startedTimestamp, now, 0);
            return maximum - Stopwatch.GetElapsedTime(started == 0 ? now : started);
        }
        public void MarkPlayable() { lock (_gate) _isPlayable = true; }
        public void SetProgress(double? progress, double? speed)
        {
            lock (_gate)
            {
                _progress = progress;
                if (speed is not null) _speed = speed;
            }
        }

        public void SetReady(IReadOnlyList<TranscodingSubtitle> subtitles)
        {
            lock (_gate)
            {
                _subtitles = subtitles;
                _progress = 1;
                _isPlayable = true;
                _state = TranscodingJobState.Ready;
                _error = null;
            }
        }

        public void SetSubtitles(IReadOnlyList<TranscodingSubtitle> subtitles)
        {
            lock (_gate) _subtitles = subtitles;
        }

        public void SetCanceled()
        {
            lock (_gate)
            {
                _state = TranscodingJobState.Canceled;
                _isPlayable = false;
                _error = "The transcoding job was canceled.";
            }
        }

        public void SetFailed(string error)
        {
            lock (_gate)
            {
                _state = TranscodingJobState.Failed;
                _isPlayable = false;
                _error = error;
            }
        }

        public TranscodingSessionStatus CreateStatus(
            Guid sessionId,
            string token,
            bool cacheHit,
            int? queuePosition)
        {
            lock (_gate)
                return new TranscodingSessionStatus(
                    sessionId,
                    token,
                    _state,
                    _plan?.Strategy,
                    _isPlayable,
                    cacheHit,
                    _progress,
                    _speed,
                    queuePosition,
                    _error,
                    _plan?.Video.CodecName,
                    _plan?.Audio?.CodecName,
                    _subtitles,
                    _plan?.UnsupportedSubtitleCount ?? 0);
        }

        public CacheManifest CreateManifest()
        {
            lock (_gate)
            {
                var plan = _plan ?? throw new InvalidOperationException("A completed job has no transcoding plan.");
                return new CacheManifest(
                    CacheManifestVersion,
                    plan.Strategy,
                    plan.Video.CodecName,
                    plan.Audio?.CodecName,
                    _subtitles.ToArray(),
                    plan.UnsupportedSubtitleCount);
            }
        }
    }
}
