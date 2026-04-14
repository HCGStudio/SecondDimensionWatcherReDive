using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Services;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class InferAnimationMetadataTests
{
    private Mock<IAnimationInfoRepository> _mockAnimationInfoRepo = null!;
    private Mock<IAnimationRepository> _mockAnimationRepo = null!;
    private Mock<IAnimationGroupRepository> _mockAnimationGroupRepo = null!;
    private Mock<IInferenceEngine> _mockInferenceEngine = null!;
    private InferAnimationMetadata _task = null!;
    private MethodInfo _processItemMethod = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockAnimationInfoRepo = new Mock<IAnimationInfoRepository>();
        _mockAnimationRepo = new Mock<IAnimationRepository>();
        _mockAnimationGroupRepo = new Mock<IAnimationGroupRepository>();
        _mockInferenceEngine = new Mock<IInferenceEngine>();

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
        int aiRetryCount = 0) =>
        new(id, title, description, DateTimeOffset.Now,
            "", "", Array.Empty<byte>(), "",
            false, default, default, false,
            null, null, null, null, null, null,
            false, aiRetryCount);

    private Task InvokeProcessItem(AnimationInfo item, CancellationToken cancellationToken)
    {
        return (Task)_processItemMethod.Invoke(_task, new object[]
        {
            item,
            _mockAnimationInfoRepo.Object,
            _mockAnimationRepo.Object,
            _mockAnimationGroupRepo.Object,
            _mockInferenceEngine.Object,
            cancellationToken
        })!;
    }

    [TestMethod]
    public async Task ProcessItem_InferenceReturnsNull_MarksProcessed()
    {
        var item = CreateTestInfo(Guid.NewGuid());

        _mockInferenceEngine
            .Setup(e => e.InferAsync("Test Title", "Test Description", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InferenceResult?)null);

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(
            r => r.UpdateAsync(
                It.Is<AnimationInfo>(i => i.IsAiProcessed),
                It.IsAny<CancellationToken>()), Times.Once);
        _mockAnimationRepo.Verify(
            r => r.AddAsync(It.IsAny<Animation>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockAnimationGroupRepo.Verify(
            r => r.AddAsync(It.IsAny<AnimationGroup>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ProcessItem_WithResult_CreatesAnimationAndGroup()
    {
        var item = CreateTestInfo(Guid.NewGuid(),
            title: "[SubGroup] Anime Title - 01 [1080p]",
            description: "Episode description");

        var result = new InferenceResult("12345", "SubGroup", 1, 1);

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
            r => r.UpdateAsync(
                It.Is<AnimationInfo>(i =>
                    i.IsAiProcessed &&
                    i.Season == 1 &&
                    i.Episode == 1 &&
                    i.Animation != null && i.Animation.TmdbId == "12345" &&
                    i.Group != null && i.Group.Name == "SubGroup"),
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
    public async Task ProcessItem_ExceptionIncrementsRetryCount()
    {
        var item = CreateTestInfo(Guid.NewGuid(),
            title: "Failing Title",
            description: "Failing Description");

        _mockInferenceEngine
            .Setup(e => e.InferAsync(item.Title, item.Description, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));

        await InvokeProcessItem(item, CancellationToken.None);

        _mockAnimationInfoRepo.Verify(
            r => r.UpdateAsync(
                It.Is<AnimationInfo>(i => i.AiRetryCount == 1 && !i.IsAiProcessed),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}
