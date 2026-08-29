using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Services.Transcoding;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class HlsTranscodingServiceTests
{
    [TestMethod]
    public async Task PrepareAsync_GeneratesPlayableHlsAndReusesCompletedCache()
    {
        var runner = new CompletingRunner();
        await using var fixture = await TranscodingFixture.CreateAsync(runner);

        var initial = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "episode.mkv",
            TranscodingSelection.Create("auto", "ja", null, "en", null),
            CancellationToken.None);
        var ready = await WaitForStateAsync(
            fixture.Service,
            initial,
            TranscodingJobState.Ready);

        Assert.IsTrue(ready.IsPlayable);
        Assert.AreEqual(TranscodingStrategy.Remux, ready.Strategy);
        Assert.AreEqual(1, ready.Subtitles.Count);
        Assert.AreEqual(1, runner.GenerateCalls);
        StringAssert.Contains(
            await fixture.Service.GetPlaylistAsync(
                ready.SessionId,
                ready.AccessToken,
                CancellationToken.None),
            "segment-000000.ts");
        var segment = await fixture.Service.OpenSegmentAsync(
            ready.SessionId,
            ready.AccessToken,
            "segment-000000.ts",
            CancellationToken.None);
        Assert.IsNotNull(segment);
        Assert.AreEqual("video/mp2t", segment.ContentType);
        await segment.Stream.DisposeAsync();

        var repeated = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "episode.mkv",
            TranscodingSelection.Create("auto", "ja", null, "en", null),
            CancellationToken.None);

        Assert.AreEqual(TranscodingJobState.Ready, repeated.State);
        Assert.IsTrue(repeated.CacheHit);
        Assert.AreEqual(1, runner.GenerateCalls);
        await fixture.RestartServiceAsync();
        var afterRestart = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "episode.mkv",
            TranscodingSelection.Create("auto", "ja", null, "en", null),
            CancellationToken.None);
        Assert.AreEqual(TranscodingJobState.Ready, afterRestart.State);
        Assert.IsTrue(afterRestart.CacheHit);
        Assert.AreEqual(1, runner.GenerateCalls);
        var metrics = await fixture.Service.GetMetricsAsync(CancellationToken.None);
        Assert.AreEqual(1, metrics.CompletedJobs);
        Assert.AreEqual(2, metrics.CacheHits);
        Assert.IsTrue(metrics.CacheBytes > 0);
    }

    [TestMethod]
    public async Task PrepareAsync_SourceVersionChangeDoesNotReuseOldSegments()
    {
        var runner = new CompletingRunner();
        await using var fixture = await TranscodingFixture.CreateAsync(runner);

        var first = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "episode.mkv",
            TranscodingSelection.Create("auto", null, null, null, null),
            CancellationToken.None);
        await WaitForStateAsync(fixture.Service, first, TranscodingJobState.Ready);
        fixture.LastModifiedUtc = fixture.LastModifiedUtc.AddSeconds(1);

        var changed = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "episode.mkv",
            TranscodingSelection.Create("auto", null, null, null, null),
            CancellationToken.None);
        await WaitForStateAsync(fixture.Service, changed, TranscodingJobState.Ready);

        Assert.IsFalse(changed.CacheHit);
        Assert.AreEqual(2, runner.GenerateCalls);
    }

    [TestMethod]
    public async Task PrepareAsync_ConcurrentLimitQueuesAndRejectsOnlyWhenBoundedQueueIsFull()
    {
        var runner = new BlockingRunner();
        await using var fixture = await TranscodingFixture.CreateAsync(
            runner,
            queueCapacity: 1,
            relativePaths: ["one.mkv", "two.mkv", "three.mkv"]);

        var first = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "one.mkv",
            TranscodingSelection.Create("auto", null, null, null, null),
            CancellationToken.None);
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "two.mkv",
            TranscodingSelection.Create("auto", null, null, null, null),
            CancellationToken.None);

        Assert.AreEqual(TranscodingJobState.Queued, second.State);
        Assert.AreEqual(1, second.QueuePosition);
        await Assert.ThrowsExactlyAsync<TranscodingQueueFullException>(() =>
            fixture.Service.PrepareAsync(
                fixture.AnimationInfoId,
                "three.mkv",
                TranscodingSelection.Create("auto", null, null, null, null),
                CancellationToken.None));
        var metrics = await fixture.Service.GetMetricsAsync(CancellationToken.None);
        Assert.AreEqual(1, metrics.ActiveJobs);
        Assert.AreEqual(1, metrics.QueuedJobs);

        Assert.IsTrue(await fixture.Service.CancelAsync(
            first.SessionId,
            first.AccessToken,
            CancellationToken.None));
        Assert.IsTrue(await fixture.Service.CancelAsync(
            second.SessionId,
            second.AccessToken,
            CancellationToken.None));
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        TranscodingMetricsSnapshot afterCancellation;
        do
        {
            afterCancellation = await fixture.Service.GetMetricsAsync(cleanupTimeout.Token);
            if (afterCancellation.CanceledJobs == 2
                && !Directory.EnumerateDirectories(fixture.CachePath).Any()) break;
            await Task.Delay(10, cleanupTimeout.Token);
        } while (true);
        Assert.AreEqual(2, afterCancellation.CanceledJobs);
    }

    [TestMethod]
    public async Task FailedJobDeletesPartialOutputAndReportsFailureRate()
    {
        await using var fixture = await TranscodingFixture.CreateAsync(new FailingRunner());
        var initial = await fixture.Service.PrepareAsync(
            fixture.AnimationInfoId,
            "episode.mkv",
            TranscodingSelection.Create("auto", null, null, null, null),
            CancellationToken.None);

        var failed = await WaitForStateAsync(
            fixture.Service,
            initial,
            TranscodingJobState.Failed);

        StringAssert.Contains(failed.Error, "fixture FFmpeg failure");
        Assert.IsFalse(Directory.EnumerateDirectories(fixture.CachePath).Any());
        var metrics = await fixture.Service.GetMetricsAsync(CancellationToken.None);
        Assert.AreEqual(1, metrics.FailedJobs);
        Assert.AreEqual(1, metrics.FailureRate);
    }

    private static async Task<TranscodingSessionStatus> WaitForStateAsync(
        IHlsTranscodingService service,
        TranscodingSessionStatus session,
        TranscodingJobState expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var current = await service.GetStatusAsync(
                session.SessionId,
                session.AccessToken,
                timeout.Token);
            Assert.IsNotNull(current);
            if (current.State == expected) return current;
            if (current.State is TranscodingJobState.Failed or TranscodingJobState.Canceled)
                Assert.Fail($"Transcoding ended in {current.State}: {current.Error}");
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class CompletingRunner : IFfmpegProcessRunner
    {
        private int _generateCalls;
        public int GenerateCalls => Volatile.Read(ref _generateCalls);

        public Task<MediaProbe> ProbeAsync(Stream source, CancellationToken cancellationToken)
            => Task.FromResult(new MediaProbe(
                "matroska",
                TimeSpan.FromSeconds(30),
                [
                    new MediaStreamProbe(0, "video", "h264", null, null, true, false, false),
                    new MediaStreamProbe(1, "audio", "aac", "jpn", "Japanese", true, false, false),
                    new MediaStreamProbe(2, "subtitle", "ass", "eng", "English", true, false, false)
                ]));

        public async Task<FfmpegRunResult> GenerateHlsAsync(
            Stream source,
            TranscodingPlan plan,
            TranscodingSelection selection,
            string outputDirectory,
            bool useHardwareEncoder,
            Action<FfmpegProgress> onProgress,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _generateCalls);
            await File.WriteAllBytesAsync(
                Path.Combine(outputDirectory, "segment-000000.ts"),
                [1, 2, 3],
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "media.m3u8"),
                "#EXTM3U\n#EXTINF:6,\nsegment-000000.ts\n#EXT-X-ENDLIST\n",
                cancellationToken);
            onProgress(new FfmpegProgress(30, 2, true));
            return new FfmpegRunResult(0, string.Empty);
        }

        public async Task<IReadOnlyList<TranscodingSubtitle>> ExtractTextSubtitlesAsync(
            Stream source,
            TranscodingPlan plan,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            const string name = "subtitle-2.vtt";
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, name),
                "WEBVTT\n",
                cancellationToken);
            return [new TranscodingSubtitle(name, "English", "eng", "vtt")];
        }
    }

    private sealed class BlockingRunner : IFfmpegProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MediaProbe> ProbeAsync(Stream source, CancellationToken cancellationToken)
            => Task.FromResult(new MediaProbe(
                "matroska",
                TimeSpan.FromSeconds(30),
                [
                    new MediaStreamProbe(0, "video", "h264", null, null, true, false, false),
                    new MediaStreamProbe(1, "audio", "aac", null, null, true, false, false)
                ]));

        public async Task<FfmpegRunResult> GenerateHlsAsync(
            Stream source,
            TranscodingPlan plan,
            TranscodingSelection selection,
            string outputDirectory,
            bool useHardwareEncoder,
            Action<FfmpegProgress> onProgress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "partial.tmp"),
                "partial",
                cancellationToken);
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new FfmpegRunResult(0, string.Empty);
        }

        public Task<IReadOnlyList<TranscodingSubtitle>> ExtractTextSubtitlesAsync(
            Stream source,
            TranscodingPlan plan,
            string outputDirectory,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TranscodingSubtitle>>([]);
    }

    private sealed class FailingRunner : IFfmpegProcessRunner
    {
        public Task<MediaProbe> ProbeAsync(Stream source, CancellationToken cancellationToken)
            => Task.FromResult(new MediaProbe(
                "matroska",
                TimeSpan.FromSeconds(30),
                [
                    new MediaStreamProbe(0, "video", "h264", null, null, true, false, false),
                    new MediaStreamProbe(1, "audio", "aac", null, null, true, false, false)
                ]));

        public async Task<FfmpegRunResult> GenerateHlsAsync(
            Stream source,
            TranscodingPlan plan,
            TranscodingSelection selection,
            string outputDirectory,
            bool useHardwareEncoder,
            Action<FfmpegProgress> onProgress,
            CancellationToken cancellationToken)
        {
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "partial.tmp"),
                "partial",
                cancellationToken);
            return new FfmpegRunResult(1, "fixture FFmpeg failure");
        }

        public Task<IReadOnlyList<TranscodingSubtitle>> ExtractTextSubtitlesAsync(
            Stream source,
            TranscodingPlan plan,
            string outputDirectory,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TranscodingSubtitle>>([]);
    }

    private sealed class TranscodingFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly TranscodingMetrics _metrics;
        private readonly string _cachePath;
        private readonly IFfmpegProcessRunner _runner;
        private readonly IOptions<TranscodingOptions> _options;

        private TranscodingFixture(
            ServiceProvider provider,
            TranscodingMetrics metrics,
            IFfmpegProcessRunner runner,
            IOptions<TranscodingOptions> options,
            HlsTranscodingService service,
            string cachePath,
            Guid animationInfoId)
        {
            _provider = provider;
            _metrics = metrics;
            _runner = runner;
            _options = options;
            Service = service;
            _cachePath = cachePath;
            AnimationInfoId = animationInfoId;
        }

        public HlsTranscodingService Service { get; private set; }
        public Guid AnimationInfoId { get; }
        public string CachePath => _cachePath;
        public DateTimeOffset LastModifiedUtc { get; set; } =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static async Task<TranscodingFixture> CreateAsync(
            IFfmpegProcessRunner runner,
            int queueCapacity = 8,
            IReadOnlyList<string>? relativePaths = null)
        {
            var cachePath = Path.Combine(Path.GetTempPath(), $"sdw-transcoding-test-{Guid.NewGuid():N}");
            var animationInfoId = Guid.NewGuid();
            var animation = new Animation(Guid.NewGuid(), "42", "Anime", "Anime", null);
            var group = new AnimationGroup(Guid.NewGuid(), "Group");
            var info = new AnimationInfo(
                animationInfoId,
                "Episode",
                string.Empty,
                DateTimeOffset.UtcNow,
                string.Empty,
                string.Empty,
                [],
                string.Empty,
                false,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                true,
                "test",
                "/physical",
                1,
                1,
                group,
                animation,
                true,
                0);
            relativePaths ??= ["episode.mkv"];
            var mappings = relativePaths.ToDictionary(
                relative => $"/Anime/Group/{relative}",
                relative => new FileMapping(
                    Guid.NewGuid(),
                    animationInfoId,
                    $"/Anime/Group/{relative}",
                    $"/physical/{relative}",
                    "test"));

            var animationRepository = new Mock<IAnimationInfoRepository>();
            animationRepository.Setup(repository => repository.FindByIdWithAnimationAsync(
                    animationInfoId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(info);
            var mappingRepository = new Mock<IFileMappingRepository>();
            mappingRepository.Setup(repository => repository.FindByVirtualPathAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, CancellationToken _) =>
                    mappings.GetValueOrDefault(path));
            var store = new Mock<IFileStore>();
            store.SetupGet(item => item.Name).Returns("test");
            var storeProvider = new Mock<IFileStoreProvider>();
            storeProvider.Setup(provider => provider.GetRequiredClient("test")).Returns(store.Object);
            storeProvider.Setup(provider => provider.GetClient("test")).Returns(store.Object);

            var services = new ServiceCollection();
            services.AddSingleton(animationRepository.Object);
            services.AddSingleton(mappingRepository.Object);
            services.AddSingleton(store.Object);
            services.AddSingleton(storeProvider.Object);
            var provider = services.BuildServiceProvider();
            var options = Options.Create(new TranscodingOptions
            {
                CachePath = cachePath,
                MaxConcurrentJobs = 1,
                QueueCapacity = queueCapacity,
                CleanupInterval = TimeSpan.FromHours(1),
                CacheTtl = TimeSpan.FromDays(1),
                SessionTtl = TimeSpan.FromHours(1)
            });
            var metrics = new TranscodingMetrics();
            var service = new HlsTranscodingService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                runner,
                metrics,
                new FileExtensionContentTypeProvider(),
                options,
                NullLogger<HlsTranscodingService>.Instance);
            var fixture = new TranscodingFixture(
                provider,
                metrics,
                runner,
                options,
                service,
                cachePath,
                animationInfoId);
            store.Setup(item => item.FileInfoAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string path, CancellationToken _) => new FileStoreInfo(
                    false,
                    path,
                    Path.GetFileName(path),
                    1024,
                    fixture.LastModifiedUtc));
            store.Setup(item => item.OpenReadStreamAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
            await service.StartAsync(CancellationToken.None);
            return fixture;
        }

        public async Task RestartServiceAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            Service = new HlsTranscodingService(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                _runner,
                _metrics,
                new FileExtensionContentTypeProvider(),
                _options,
                NullLogger<HlsTranscodingService>.Instance);
            await Service.StartAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(CancellationToken.None);
            Service.Dispose();
            _metrics.Dispose();
            await _provider.DisposeAsync();
            if (Directory.Exists(_cachePath)) Directory.Delete(_cachePath, recursive: true);
        }
    }
}
