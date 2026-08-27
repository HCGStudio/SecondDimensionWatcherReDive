using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;
using PolicyRecord = SecondDimensionWatcherReDive.Framework.DataRepository.SubscriptionAutomationPolicy;
using UpsertPolicyRequest = SecondDimensionWatcherReDive.Controllers.External.UpsertSubscriptionAutomationPolicyRequest;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SubscriptionPoliciesControllerTests
{
    private readonly Guid _feedId = Guid.NewGuid();
    private Mock<ISubscriptionAutomationPolicyRepository> _policyRepository = null!;
    private Mock<IFeedRepository> _feedRepository = null!;
    private Mock<ISubscriptionAutomationSimulationService> _simulationService = null!;
    private SubscriptionPoliciesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _policyRepository = new Mock<ISubscriptionAutomationPolicyRepository>();
        _feedRepository = new Mock<IFeedRepository>();
        _simulationService = new Mock<ISubscriptionAutomationSimulationService>();
        _feedRepository
            .Setup(repository => repository.FindByIdAsync(_feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Feed(_feedId, "https://example.com/feed", "Example", DateTimeOffset.UtcNow));
        _policyRepository
            .Setup(repository => repository.UpsertAsync(
                It.IsAny<PolicyRecord>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyRecord policy, CancellationToken _) => policy);
        _controller = new SubscriptionPoliciesController(
            _policyRepository.Object,
            _feedRepository.Object,
            _simulationService.Object);
    }

    [TestMethod]
    public async Task UpsertPolicy_ReturnsNotFoundWhenFeedDoesNotExist()
    {
        _feedRepository
            .Setup(repository => repository.FindByIdAsync(_feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Feed?)null);

        var result = await _controller.UpsertPolicy(
            _feedId,
            ValidRequest(),
            CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundObjectResult>(result);
        _policyRepository.Verify(repository => repository.UpsertAsync(
            It.IsAny<PolicyRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpsertPolicy_RejectsInvalidMode()
    {
        var request = ValidRequest() with { Mode = "autodownload" };

        var result = await _controller.UpsertPolicy(_feedId, request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        _policyRepository.Verify(repository => repository.UpsertAsync(
            It.IsAny<PolicyRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow(-1L, null)]
    [DataRow(null, -1L)]
    [DataRow(200L, 100L)]
    public async Task UpsertPolicy_RejectsInvalidSizeRange(long? minSizeBytes, long? maxSizeBytes)
    {
        var request = ValidRequest() with
        {
            MinSizeBytes = minSizeBytes,
            MaxSizeBytes = maxSizeBytes
        };

        var result = await _controller.UpsertPolicy(_feedId, request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        _policyRepository.Verify(repository => repository.UpsertAsync(
            It.IsAny<PolicyRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UpsertPolicy_TrimsAndDeduplicatesFilterValues()
    {
        PolicyRecord? captured = null;
        _policyRepository
            .Setup(repository => repository.UpsertAsync(
                It.IsAny<PolicyRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<PolicyRecord, CancellationToken>((policy, _) => captured = policy)
            .ReturnsAsync((PolicyRecord policy, CancellationToken _) => policy);

        var request = ValidRequest() with
        {
            SubtitleGroups = ["  SubsPlease  ", "subsplease", "Lilith-Raws"],
            Resolutions = ["1080p", " 1080P "],
            ExcludedKeywords = ["  v2  ", "V2", "  batch "]
        };

        var result = await _controller.UpsertPolicy(_feedId, request, CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(
            new[] { "SubsPlease", "Lilith-Raws" },
            captured.SubtitleGroups.ToArray());
        CollectionAssert.AreEqual(new[] { "1080p" }, captured.Resolutions.ToArray());
        CollectionAssert.AreEqual(new[] { "v2", "batch" }, captured.ExcludedKeywords.ToArray());
    }

    [TestMethod]
    public async Task UpsertPolicy_PreservesCreatedAtAndReturnsSavedPolicy()
    {
        var createdAt = DateTimeOffset.UtcNow.AddDays(-2);
        var existing = Policy(createdAt, createdAt.AddDays(1));
        _policyRepository
            .Setup(repository => repository.FindByFeedIdAsync(_feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await _controller.UpsertPolicy(
            _feedId,
            ValidRequest() with { Mode = "AutoDownload" },
            CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var response = ok.Value as Controllers.External.SubscriptionAutomationPolicy;
        Assert.IsNotNull(response);
        Assert.AreEqual("AutoDownload", response.Mode);
        Assert.AreEqual(createdAt, response.CreatedAt);
        Assert.IsTrue(response.UpdatedAt > existing.UpdatedAt);
    }

    [TestMethod]
    public async Task DeletePolicy_ReturnsNoContentWhenDeleted()
    {
        _policyRepository
            .Setup(repository => repository.DeleteByFeedIdAsync(_feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.DeletePolicy(_feedId, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
    }

    [TestMethod]
    public async Task DeletePolicy_ReturnsNotFoundWhenMissing()
    {
        _policyRepository
            .Setup(repository => repository.DeleteByFeedIdAsync(_feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.DeletePolicy(_feedId, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task SimulatePolicy_UsesNormalizedDraftWithoutPersistingIt()
    {
        PolicyRecord? captured = null;
        var simulation = new SubscriptionAutomationSimulationResult(0, 0, []);
        _simulationService
            .Setup(service => service.SimulateAsync(
                It.IsAny<PolicyRecord>(),
                It.IsAny<CancellationToken>()))
            .Callback<PolicyRecord, CancellationToken>((policy, _) => captured = policy)
            .ReturnsAsync(simulation);
        var request = ValidRequest() with
        {
            SubtitleGroups = ["  SubsPlease ", "subsplease"],
            Mode = "NotifyOnly"
        };

        var result = await _controller.SimulatePolicy(_feedId, request, CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var response = ok.Value as Controllers.External.SubscriptionAutomationSimulationResult;
        Assert.IsNotNull(response);
        Assert.AreEqual(simulation.Total, response.Total);
        Assert.AreEqual(simulation.Matched, response.Matched);
        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(new[] { "SubsPlease" }, captured.SubtitleGroups.ToArray());
        Assert.AreEqual(SubscriptionAutomationMode.NotifyOnly, captured.Mode);
        _policyRepository.Verify(repository => repository.UpsertAsync(
            It.IsAny<PolicyRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SimulatePolicy_ReturnsNotFoundWhenFeedDoesNotExist()
    {
        _feedRepository
            .Setup(repository => repository.FindByIdAsync(_feedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Feed?)null);

        var result = await _controller.SimulatePolicy(
            _feedId,
            ValidRequest(),
            CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundObjectResult>(result);
        _simulationService.Verify(service => service.SimulateAsync(
            It.IsAny<PolicyRecord>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UpsertPolicyRequest ValidRequest() =>
        new(
            ["SubsPlease"],
            ["1080p"],
            ["HEVC"],
            ["zh-Hans"],
            100,
            1_000,
            ["batch"],
            "ManualConfirm");

    private PolicyRecord Policy(DateTimeOffset createdAt, DateTimeOffset updatedAt) =>
        new(
            _feedId,
            ["SubsPlease"],
            ["1080p"],
            ["HEVC"],
            ["zh-Hans"],
            100,
            1_000,
            ["batch"],
            SubscriptionAutomationMode.ManualConfirm,
            createdAt,
            updatedAt);
}
