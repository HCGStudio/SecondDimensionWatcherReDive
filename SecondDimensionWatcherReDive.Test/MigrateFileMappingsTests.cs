using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Tasks;
using SecondDimensionWatcherReDive.MigrationTasks;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class MigrateFileMappingsTests
{
    [TestMethod]
    public async Task ExecuteAsync_MappingFailure_ThrowsWithoutAdvancingCheckpoint()
    {
        var info = CreateInfo();
        var animationRepository = new Mock<IAnimationInfoRepository>();
        animationRepository.Setup(repository => repository.GetDownloadedMigrationBatchAsync(
                null,
                null,
                50,
                CancellationToken.None))
            .ReturnsAsync([info]);
        var mappingRepository = new Mock<IFileMappingRepository>();
        mappingRepository.Setup(repository => repository.ExistsForAnimationInfoAsync(
                info.Id,
                CancellationToken.None))
            .ReturnsAsync(false);
        var mapper = new Mock<IFileMapper>();
        mapper.Setup(candidate => candidate.MapDownloadAsync(info.Id, CancellationToken.None))
            .ReturnsAsync(false);
        using var provider = CreateProvider(
            animationRepository.Object,
            mappingRepository.Object,
            mapper.Object);
        var migration = CreateMigration(provider);
        var savedCheckpoints = new List<string?>();
        var context = new MigrationExecutionContext(
            null,
            (checkpoint, _) =>
            {
                savedCheckpoints.Add(checkpoint);
                return Task.CompletedTask;
            });

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => migration.ExecuteAsync(context, CancellationToken.None));

        StringAssert.Contains(exception.Message, info.Id.ToString());
        Assert.IsEmpty(savedCheckpoints);
    }

    [TestMethod]
    public async Task ExecuteAsync_CompletedBatch_SavesCursorAndResumeUsesIt()
    {
        var info = CreateInfo();
        var firstAnimationRepository = new Mock<IAnimationInfoRepository>();
        firstAnimationRepository.Setup(repository => repository.GetDownloadedMigrationBatchAsync(
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<Guid?>(),
                50,
                CancellationToken.None))
            .ReturnsAsync((DateTimeOffset? time, Guid? id, int _, CancellationToken _) =>
                time is null && id is null ? [info] : []);
        var mappingRepository = new Mock<IFileMappingRepository>();
        mappingRepository.Setup(repository => repository.ExistsForAnimationInfoAsync(
                info.Id,
                CancellationToken.None))
            .ReturnsAsync(false);
        var mapper = new Mock<IFileMapper>();
        mapper.Setup(candidate => candidate.MapDownloadAsync(info.Id, CancellationToken.None))
            .ReturnsAsync(true);
        using var firstProvider = CreateProvider(
            firstAnimationRepository.Object,
            mappingRepository.Object,
            mapper.Object);
        string? checkpoint = null;
        var firstContext = new MigrationExecutionContext(
            null,
            (value, _) =>
            {
                checkpoint = value;
                return Task.CompletedTask;
            });

        await CreateMigration(firstProvider).ExecuteAsync(
            firstContext,
            CancellationToken.None);

        Assert.IsNotNull(checkpoint);
        DateTimeOffset? resumedTime = null;
        Guid? resumedId = null;
        var resumedAnimationRepository = new Mock<IAnimationInfoRepository>();
        resumedAnimationRepository.Setup(repository => repository.GetDownloadedMigrationBatchAsync(
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<Guid?>(),
                50,
                CancellationToken.None))
            .Callback((DateTimeOffset? time, Guid? id, int _, CancellationToken _) =>
            {
                resumedTime = time;
                resumedId = id;
            })
            .ReturnsAsync([]);
        using var resumedProvider = CreateProvider(
            resumedAnimationRepository.Object,
            Mock.Of<IFileMappingRepository>(),
            Mock.Of<IFileMapper>());
        var resumedContext = new MigrationExecutionContext(
            checkpoint,
            (_, _) => Task.CompletedTask);

        await CreateMigration(resumedProvider).ExecuteAsync(
            resumedContext,
            CancellationToken.None);

        Assert.AreEqual(info.PublishTime, resumedTime);
        Assert.AreEqual(info.Id, resumedId);
        mapper.Verify(candidate => candidate.MapDownloadAsync(
            info.Id,
            CancellationToken.None), Times.Once);
    }

    private static MigrateFileMappings CreateMigration(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<MigrateFileMappings>.Instance);

    private static ServiceProvider CreateProvider(
        IAnimationInfoRepository animationRepository,
        IFileMappingRepository mappingRepository,
        IFileMapper mapper) => new ServiceCollection()
        .AddSingleton(animationRepository)
        .AddSingleton(mappingRepository)
        .AddSingleton(mapper)
        .BuildServiceProvider();

    private static AnimationInfo CreateInfo() => new(
        Guid.NewGuid(),
        "Title",
        "Description",
        DateTimeOffset.UtcNow,
        "https://example.test/item.torrent",
        "torrent",
        [],
        "hash",
        true,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        true,
        "local",
        "/downloads/item",
        1,
        1,
        null,
        null,
        true,
        0);
}
