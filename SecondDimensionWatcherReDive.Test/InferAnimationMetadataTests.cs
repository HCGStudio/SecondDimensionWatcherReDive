using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class InferAnimationMetadataTests
{
    private Mock<IAnimationInfoRepository> _mockAnimationInfoRepo = null!;
    private Mock<IAnimationRepository> _mockAnimationRepo = null!;
    private Mock<IAnimationGroupRepository> _mockAnimationGroupRepo = null!;
    private Mock<IInferenceEngine> _mockInferenceEngine = null!;
    private Mock<IFileMapper> _mockFileMapper = null!;
    private InferAnimationMetadata _task = null!;
    private MethodInfo _processItemMethod = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockAnimationInfoRepo = new Mock<IAnimationInfoRepository>();
        _mockAnimationRepo = new Mock<IAnimationRepository>();
        _mockAnimationGroupRepo = new Mock<IAnimationGroupRepository>();
        _mockInferenceEngine = new Mock<IInferenceEngine>();
        _mockFileMapper = new Mock<IFileMapper>();
        _mockAnimationInfoRepo
            .Setup(repository => repository.TryUpdateAsync(
                It.IsAny<AnimationInfo>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();

        // TmdbTool is a concrete class without virtual methods.
        // Construct it with a TMDbClient pointed at a non-listening local port so any
        // HTTP call fails immediately with connection-refused. TmdbTool catches all
        // exceptions internally and returns null, which is safe for tests.
        var tmdbClient = new TMDbClient("fake-key", false, "127.0.0.1:1");
        var tmdbTool = new TmdbTool(tmdbClient, Mock.Of<ILogger<TmdbTool>>());

        _task = new InferAnimationMetadata(
            mockScopeFactory.Object,
            tmdbTool,
            Mock.Of<ILogger<InferAnimationMetadata>>());

        _processItemMethod = typeof(InferAnimationMetadata)
            .GetMethod("ProcessItem", BindingFlags.NonPublic | BindingFlags.Instance)!;
    }

    private static AnimationInfo CreateTestInfo(
        Guid id,
        string title = "Test Title",
        string description = "Test Description",
        int aiRetryCount = 0,
        bool isDownloadFinished = false,
        long stateVersion = 11) =>
        new(id, title, description, DateTimeOffset.Now,
            "", "", Array.Empty<byte>(), "",
            false, default, default, isDownloadFinished,
            null, null, null, null, null, null,
            false, aiRetryCount,
            StateVersion: stateVersion);

    private Task InvokeProcessItem(AnimationInfo item, CancellationToken cancellationToken)
    {
        return (Task)_processItemMethod.Invoke(_task, new object[]
        {
            item,
            _mockAnimationInfoRepo.Object,
            _mockAnimationRepo.Object,
            _mockAnimationGroupRepo.Object,
            _mockInferenceEngine.Object,
            _mockFileMapper.Object,
            cancellationToken
        })!;
    }

    [TestMethod]
    public async Task ProcessItem_InferenceReturnsNull_LeavesPendingAndIncrementsRetry()
    {
        var item = CreateTestInfo(Guid.NewGuid());

        _mockInferenceEngine
            .Setup(e => e.InferAsync("Test Title", "Test Description", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InferenceResult?)null);

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(
            r => r.TryUpdateAsync(
                It.Is<AnimationInfo>(i =>
                    !i.IsAiProcessed &&
                    i.AiRetryCount == 1 &&
                    i.MetadataStatus == MetadataReviewStatus.Pending &&
                    i.MetadataConfidence == null &&
                    i.MetadataLastError != null),
                item.StateVersion,
                It.IsAny<CancellationToken>()), Times.Once);
        _mockAnimationInfoRepo.Verify(
            r => r.UpdateAsync(It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockAnimationRepo.Verify(
            r => r.AddAsync(It.IsAny<Animation>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockAnimationGroupRepo.Verify(
            r => r.AddAsync(It.IsAny<AnimationGroup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessItem_WithHighConfidenceResult_CreatesAnimationAndGroupAndMarksIdentified()
    {
        var item = CreateTestInfo(Guid.NewGuid(),
            title: "[SubGroup] Anime Title - 01 [1080p]",
            description: "Episode description");

        var result = new InferenceResult("12345", "SubGroup", 1, 1, 0.91);

        _mockInferenceEngine
            .Setup(e => e.InferAsync(item.Title, item.Description, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        // TmdbTool.GetLocalizedDetailsAsync will fail (fake TMDbClient on closed port)
        // and return null. Animation is still created with fallback values.
        _mockAnimationRepo
            .Setup(r => r.FindByTmdbIdAsync("12345", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Animation?)null);

        _mockAnimationGroupRepo
            .Setup(r => r.FindByNameAsync("SubGroup", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnimationGroup?)null);

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(
            r => r.TryUpdateAsync(
                It.Is<AnimationInfo>(i =>
                    i.IsAiProcessed &&
                    i.MetadataStatus == MetadataReviewStatus.Identified &&
                    i.MetadataConfidence == 0.91 &&
                    i.MetadataLastError == null &&
                    i.Season == 1 &&
                    i.Episode == 1 &&
                    i.Animation != null && i.Animation.TmdbId == "12345" &&
                    i.Group != null && i.Group.Name == "SubGroup"),
                item.StateVersion,
                It.IsAny<CancellationToken>()), Times.Once);

        _mockAnimationRepo.Verify(
            r => r.AddAsync(
                It.Is<Animation>(a => a.TmdbId == "12345"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _mockAnimationGroupRepo.Verify(
            r => r.AddAsync(
                It.Is<AnimationGroup>(g => g.Name == "SubGroup"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ProcessItem_WithLowConfidenceResult_MarksLowConfidence()
    {
        var item = CreateTestInfo(Guid.NewGuid());
        _mockInferenceEngine
            .Setup(e => e.InferAsync(item.Title, item.Description, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InferenceResult(null, null, 2, 4, 0.42));

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(updated =>
                updated.IsAiProcessed &&
                updated.MetadataStatus == MetadataReviewStatus.LowConfidence &&
                updated.MetadataConfidence == 0.42 &&
                updated.Season == 2 &&
                updated.Episode == 4),
            item.StateVersion,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ProcessItem_ThirdFailure_MarksFailed()
    {
        var item = CreateTestInfo(Guid.NewGuid(),
            title: "Failing Title",
            description: "Failing Description",
            aiRetryCount: 2);

        _mockInferenceEngine
            .Setup(e => e.InferAsync(item.Title, item.Description, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(
            r => r.TryUpdateAsync(
                It.Is<AnimationInfo>(i =>
                    i.AiRetryCount == 3 &&
                    !i.IsAiProcessed &&
                    i.MetadataStatus == MetadataReviewStatus.Failed &&
                    i.MetadataLastError == "AI service unavailable"),
                item.StateVersion,
                It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ProcessItem_StaleSuccessfulInference_DoesNotRemapDownloadedFiles()
    {
        var item = CreateTestInfo(
            Guid.NewGuid(),
            isDownloadFinished: true,
            stateVersion: 42);
        _mockInferenceEngine
            .Setup(engine => engine.InferAsync(
                item.Title,
                item.Description,
                CancellationToken.None))
            .ReturnsAsync(new InferenceResult(null, null, 1, 1, 0.95));
        _mockAnimationInfoRepo
            .Setup(repository => repository.TryUpdateAsync(
                It.IsAny<AnimationInfo>(),
                item.StateVersion,
                CancellationToken.None))
            .ReturnsAsync(false);

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(updated =>
                updated.MetadataStatus == MetadataReviewStatus.Identified),
            item.StateVersion,
            CancellationToken.None), Times.Once);
        _mockFileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
