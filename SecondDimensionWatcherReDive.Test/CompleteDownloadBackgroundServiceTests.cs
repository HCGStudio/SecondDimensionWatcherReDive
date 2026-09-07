using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Notifications;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class CompleteDownloadBackgroundServiceTests
{
    [TestMethod]
    public async Task ProcessClaimedJobAsync_ResumesAtPersistedStage()
    {
        var job = CreateJob(DurableJobStage.Notify);
        var payload = JsonSerializer.Deserialize<DownloadCompletionJobPayload>(job.PayloadJson)!;
        var repository = new Mock<IDurableJobRepository>();
        repository.Setup(candidate => candidate.AdvanceStageAsync(
                job.Id,
                It.IsAny<string>(),
                It.IsAny<DurableJobStage>(),
                It.IsAny<DurableJobStage>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var mapper = new Mock<IFileMapper>();
        var notifier = new Mock<IDownloadCompletionNotifier>();
        var plugin = new Mock<IPluginEventTrigger<FileDownloadCompleteParam>>();
        using var provider = CreateProvider(
            repository.Object,
            mapper.Object,
            notifier.Object,
            plugin.Object);
        var service = CreateService(provider);

        await service.ProcessClaimedJobAsync(
            provider,
            repository.Object,
            job,
            CancellationToken.None);

        mapper.Verify(candidate => candidate.MapDownloadAsync(
            It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        notifier.Verify(candidate => candidate.NotifyAsync(
            job.Id,
            It.Is<DownloadCompletionJobPayload>(value => value == payload),
            It.IsAny<CancellationToken>()), Times.Once);
        plugin.Verify(candidate => candidate.InvokeAsync(
            It.Is<FileDownloadCompleteParam>(value =>
                value.EventId == job.Id
                && value.ItemId == payload.ItemId
                && value.StorePath == payload.StorePath
                && value.FileStore == payload.FileStore),
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(candidate => candidate.AdvanceStageAsync(
            job.Id,
            It.IsAny<string>(),
            DurableJobStage.Notify,
            DurableJobStage.InvokePlugins,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(candidate => candidate.AdvanceStageAsync(
            job.Id,
            It.IsAny<string>(),
            DurableJobStage.InvokePlugins,
            DurableJobStage.Done,
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessClaimedJobAsync_PluginStageDoesNotReplayPriorEffects()
    {
        var job = CreateJob(DurableJobStage.InvokePlugins);
        var repository = new Mock<IDurableJobRepository>();
        repository.Setup(candidate => candidate.AdvanceStageAsync(
                job.Id,
                It.IsAny<string>(),
                DurableJobStage.InvokePlugins,
                DurableJobStage.Done,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var mapper = new Mock<IFileMapper>();
        var notifier = new Mock<IDownloadCompletionNotifier>();
        var plugin = new Mock<IPluginEventTrigger<FileDownloadCompleteParam>>();
        using var provider = CreateProvider(
            repository.Object,
            mapper.Object,
            notifier.Object,
            plugin.Object);

        await CreateService(provider).ProcessClaimedJobAsync(
            provider,
            repository.Object,
            job,
            CancellationToken.None);

        mapper.VerifyNoOtherCalls();
        notifier.VerifyNoOtherCalls();
        plugin.Verify(candidate => candidate.InvokeAsync(
            It.Is<FileDownloadCompleteParam>(value => value.EventId == job.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessClaimedJobAsync_FailureSchedulesExponentialRetry()
    {
        var job = CreateJob(DurableJobStage.MapFiles, attemptCount: 2);
        var repository = new Mock<IDurableJobRepository>();
        var mapper = new Mock<IFileMapper>();
        mapper.Setup(candidate => candidate.MapDownloadAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        using var provider = CreateProvider(
            repository.Object,
            mapper.Object,
            Mock.Of<IDownloadCompletionNotifier>(),
            Mock.Of<IPluginEventTrigger<FileDownloadCompleteParam>>());
        var service = CreateService(provider);

        await service.ProcessClaimedJobAsync(
            provider,
            repository.Object,
            job,
            CancellationToken.None);

        repository.Verify(candidate => candidate.MarkFailedAsync(
            job.Id,
            It.IsAny<string>(),
            3,
            It.IsAny<DateTimeOffset>(),
            It.Is<DateTimeOffset?>(retry => retry.HasValue),
            It.Is<string>(error => error.Contains("InvalidOperationException")),
            CancellationToken.None), Times.Once);
        Assert.AreEqual(TimeSpan.FromSeconds(20),
            CompleteDownloadBackgroundService.RetryDelay(3));
    }

    [TestMethod]
    public async Task ProcessClaimedJobAsync_LastFailureEntersDeadLetter()
    {
        var job = CreateJob(
            DurableJobStage.InvokePlugins,
            CompleteDownloadBackgroundService.MaxAttempts - 1);
        var repository = new Mock<IDurableJobRepository>();
        var plugin = new Mock<IPluginEventTrigger<FileDownloadCompleteParam>>();
        plugin.Setup(candidate => candidate.InvokeAsync(
                It.IsAny<FileDownloadCompleteParam>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("plugin unavailable"));
        using var provider = CreateProvider(
            repository.Object,
            Mock.Of<IFileMapper>(),
            Mock.Of<IDownloadCompletionNotifier>(),
            plugin.Object);
        var service = CreateService(provider);

        await service.ProcessClaimedJobAsync(
            provider,
            repository.Object,
            job,
            CancellationToken.None);

        repository.Verify(candidate => candidate.MarkFailedAsync(
            job.Id,
            It.IsAny<string>(),
            CompleteDownloadBackgroundService.MaxAttempts,
            It.IsAny<DateTimeOffset>(),
            null,
            It.IsAny<string>(),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_TemporaryRepositoryFailureDoesNotStopWorker()
    {
        var repository = new Mock<IDurableJobRepository>();
        repository.SetupSequence(candidate => candidate.ClaimDueAsync(
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"))
            .ReturnsAsync([]);
        using var provider = CreateProvider(
            repository.Object,
            Mock.Of<IFileMapper>(),
            Mock.Of<IDownloadCompletionNotifier>(),
            Mock.Of<IPluginEventTrigger<FileDownloadCompleteParam>>());
        var channel = Channel.CreateBounded<DownloadCompleteRequest>(1);
        var service = new CompleteDownloadBackgroundService(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ILogger<CompleteDownloadBackgroundService>>());

        await service.StartAsync(CancellationToken.None);
        channel.Writer.TryWrite(new DownloadCompleteRequest(
            Guid.NewGuid(), "/store", "local", Guid.NewGuid()));
        await WaitUntilAsync(
            () => repository.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IDurableJobRepository.ClaimDueAsync)) >= 2,
            TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        repository.Verify(candidate => candidate.ClaimDueAsync(
            It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    private static CompleteDownloadBackgroundService CreateService(
        ServiceProvider provider) => new(
        Channel.CreateBounded<DownloadCompleteRequest>(1),
        provider.GetRequiredService<IServiceScopeFactory>(),
        Mock.Of<ILogger<CompleteDownloadBackgroundService>>());

    private static ServiceProvider CreateProvider(
        IDurableJobRepository repository,
        IFileMapper mapper,
        IDownloadCompletionNotifier notifier,
        IPluginEventTrigger<FileDownloadCompleteParam> plugin) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .AddSingleton(mapper)
            .AddSingleton(notifier)
            .AddSingleton(plugin)
            .BuildServiceProvider();

    private static DurableJob CreateJob(
        DurableJobStage stage,
        int attemptCount = 0)
    {
        var now = DateTimeOffset.UtcNow;
        return new DurableJob(
            Guid.NewGuid(),
            $"completion:{Guid.NewGuid():N}",
            DurableJobType.DownloadCompletion,
            DurableJobStatus.Processing,
            stage,
            JsonSerializer.Serialize(new DownloadCompletionJobPayload(
                Guid.NewGuid(),
                "/downloads/item",
                "local",
                Guid.NewGuid())),
            attemptCount,
            now,
            now,
            now,
            null,
            null,
            "worker",
            now.AddMinutes(1),
            null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                Assert.Fail("Timed out waiting for the worker to retry.");
            await Task.Delay(10);
        }
    }
}
