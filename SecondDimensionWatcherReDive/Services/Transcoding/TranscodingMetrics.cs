using System.Diagnostics.Metrics;

namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal sealed class TranscodingMetrics : IDisposable
{
    private readonly Meter _meter = new("SecondDimensionWatcherReDive.Transcoding", "1.0.0");
    private readonly Counter<long> _completedCounter;
    private readonly Counter<long> _failedCounter;
    private readonly Counter<long> _canceledCounter;
    private readonly Counter<long> _cacheHitCounter;
    private readonly Histogram<double> _firstSegmentHistogram;
    private readonly Histogram<double> _speedHistogram;
    private long _completed;
    private long _failed;
    private long _canceled;
    private long _cacheHits;
    private long _cacheBytes;
    private long _firstSegmentSamples;
    private long _firstSegmentMilliseconds;
    private long _speedSamples;
    private double _speedTotal;
    private readonly object _speedGate = new();
    private int _queued;
    private int _active;

    public TranscodingMetrics()
    {
        _completedCounter = _meter.CreateCounter<long>("sdw.transcoding.jobs.completed");
        _failedCounter = _meter.CreateCounter<long>("sdw.transcoding.jobs.failed");
        _canceledCounter = _meter.CreateCounter<long>("sdw.transcoding.jobs.canceled");
        _cacheHitCounter = _meter.CreateCounter<long>("sdw.transcoding.cache.hits");
        _firstSegmentHistogram = _meter.CreateHistogram<double>("sdw.transcoding.first_segment.seconds", "s");
        _speedHistogram = _meter.CreateHistogram<double>("sdw.transcoding.speed", "x");
        _meter.CreateObservableGauge("sdw.transcoding.jobs.queued", () => Volatile.Read(ref _queued));
        _meter.CreateObservableGauge("sdw.transcoding.jobs.active", () => Volatile.Read(ref _active));
        _meter.CreateObservableGauge("sdw.transcoding.cache.bytes", () => Interlocked.Read(ref _cacheBytes), "By");
    }

    public void SetQueued(int value) => Volatile.Write(ref _queued, value);
    public void SetActive(int value) => Volatile.Write(ref _active, value);
    public void SetCacheBytes(long value) => Interlocked.Exchange(ref _cacheBytes, value);

    public void RecordCompleted()
    {
        Interlocked.Increment(ref _completed);
        _completedCounter.Add(1);
    }

    public void RecordFailed()
    {
        Interlocked.Increment(ref _failed);
        _failedCounter.Add(1);
    }

    public void RecordCanceled()
    {
        Interlocked.Increment(ref _canceled);
        _canceledCounter.Add(1);
    }

    public void RecordCacheHit()
    {
        Interlocked.Increment(ref _cacheHits);
        _cacheHitCounter.Add(1);
    }

    public void RecordFirstSegment(TimeSpan elapsed)
    {
        Interlocked.Increment(ref _firstSegmentSamples);
        Interlocked.Add(ref _firstSegmentMilliseconds, (long)elapsed.TotalMilliseconds);
        _firstSegmentHistogram.Record(elapsed.TotalSeconds);
    }

    public void RecordSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0) return;
        Interlocked.Increment(ref _speedSamples);
        lock (_speedGate) _speedTotal += speed;
        _speedHistogram.Record(speed);
    }

    public TranscodingMetricsSnapshot Snapshot()
    {
        var firstSamples = Interlocked.Read(ref _firstSegmentSamples);
        var speedSamples = Interlocked.Read(ref _speedSamples);
        var completed = Interlocked.Read(ref _completed);
        var failed = Interlocked.Read(ref _failed);
        double speedTotal;
        lock (_speedGate) speedTotal = _speedTotal;
        return new TranscodingMetricsSnapshot(
            Volatile.Read(ref _queued),
            Volatile.Read(ref _active),
            completed,
            failed,
            Interlocked.Read(ref _canceled),
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cacheBytes),
            firstSamples == 0
                ? null
                : Interlocked.Read(ref _firstSegmentMilliseconds) / 1000d / firstSamples,
            speedSamples == 0 ? null : speedTotal / speedSamples,
            completed + failed == 0 ? 0 : failed / (double)(completed + failed));
    }

    public void Dispose() => _meter.Dispose();
}
