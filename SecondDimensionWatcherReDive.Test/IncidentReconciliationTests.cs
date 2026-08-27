using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.Incidents;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class IncidentReconciliationTests
{
    [TestMethod]
    public async Task ReconcileAsync_QueuedAiRetryRemainsOpenWhileMetadataIsPending()
    {
        var animationInfoId = Guid.NewGuid();
        var incident = CreateAiIncident(animationInfoId);
        var pending = CreateAnimationInfo(animationInfoId, isAiProcessed: false);
        var fixture = CreateFixture(incident, pending);

        await fixture.Service.ReconcileAsync(CancellationToken.None);

        fixture.Reporter.Verify(candidate => candidate.ResolveAsync(
            IncidentType.AiFailure,
            animationInfoId.ToString(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ReconcileAsync_ClosesAiIncidentAfterSuccessfulProcessing()
    {
        var animationInfoId = Guid.NewGuid();
        var incident = CreateAiIncident(animationInfoId);
        var processed = CreateAnimationInfo(animationInfoId, isAiProcessed: true);
        var fixture = CreateFixture(incident, processed);

        await fixture.Service.ReconcileAsync(CancellationToken.None);

        fixture.Reporter.Verify(candidate => candidate.ResolveAsync(
            IncidentType.AiFailure,
            animationInfoId.ToString(),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ReconcileAsync_ClosesMappingIncidentAfterDownloadIsCancelled()
    {
        var animationInfoId = Guid.NewGuid();
        var incident = CreateMappingIncident(animationInfoId);
        var cancelled = CreateAnimationInfo(
            animationInfoId,
            isAiProcessed: true,
            isDownloadFinished: false);
        var fixture = CreateFixture(incident, cancelled);

        await fixture.Service.ReconcileAsync(CancellationToken.None);

        fixture.Reporter.Verify(candidate => candidate.ResolveAsync(
            IncidentType.FileMappingFailure,
            animationInfoId.ToString(),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ReconcileAsync_DoesNotCloseExplicitRemapFailureBecauseOldMappingsExist()
    {
        var animationInfoId = Guid.NewGuid();
        var incident = CreateMappingIncident(animationInfoId);
        var downloaded = CreateAnimationInfo(
            animationInfoId,
            isAiProcessed: true,
            isDownloadFinished: true);
        var fixture = CreateFixture(incident, downloaded);

        await fixture.Service.ReconcileAsync(CancellationToken.None);

        fixture.Reporter.Verify(candidate => candidate.ResolveAsync(
            IncidentType.FileMappingFailure,
            animationInfoId.ToString(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ReconciliationFixture CreateFixture(
        Incident incident,
        AnimationInfo animationInfo)
    {
        var animationRepository = new Mock<IAnimationInfoRepository>();
        animationRepository.Setup(candidate => candidate.GetFailedInferenceAsync(CancellationToken.None))
            .ReturnsAsync([]);
        animationRepository.Setup(candidate => candidate.GetDownloadedWithoutFileMappingsAsync(
                CancellationToken.None))
            .ReturnsAsync([]);
        animationRepository.Setup(candidate => candidate.FindByIdAsync(
                animationInfo.Id,
                CancellationToken.None))
            .ReturnsAsync(animationInfo);

        var incidentRepository = new Mock<IIncidentRepository>();
        incidentRepository.Setup(candidate => candidate.GetOpenAsync(
                IncidentType.AiFailure,
                CancellationToken.None))
            .ReturnsAsync(incident.Type == IncidentType.AiFailure ? [incident] : []);
        incidentRepository.Setup(candidate => candidate.GetOpenAsync(
                IncidentType.FileMappingFailure,
                CancellationToken.None))
            .ReturnsAsync(incident.Type == IncidentType.FileMappingFailure ? [incident] : []);
        incidentRepository.Setup(candidate => candidate.GetOpenAsync(
                IncidentType.DownloadStalled,
                CancellationToken.None))
            .ReturnsAsync([]);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(candidate => candidate.GetService(typeof(IAnimationInfoRepository)))
            .Returns(animationRepository.Object);
        provider.Setup(candidate => candidate.GetService(typeof(IIncidentRepository)))
            .Returns(incidentRepository.Object);
        var scope = new Mock<IServiceScope>();
        scope.SetupGet(candidate => candidate.ServiceProvider).Returns(provider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(candidate => candidate.CreateScope()).Returns(scope.Object);

        var reporter = new Mock<IIncidentReporter>();
        var diskProbe = new Mock<IIncidentDiskProbe>();
        diskProbe.Setup(candidate => candidate.ProbeAsync(CancellationToken.None))
            .ReturnsAsync(new IncidentDiskProbeResult(true, "/data", 100, 100, "healthy"));
        var configuration = new ConfigurationBuilder().Build();
        var service = new IncidentReconciliationBackgroundService(
            scopeFactory.Object,
            reporter.Object,
            diskProbe.Object,
            configuration,
            Mock.Of<ILogger<IncidentReconciliationBackgroundService>>());
        return new ReconciliationFixture(service, reporter);
    }

    private static AnimationInfo CreateAnimationInfo(
        Guid id,
        bool isAiProcessed,
        bool isDownloadFinished = false) => new(
        id,
        "Title",
        "Description",
        DateTimeOffset.UtcNow,
        "https://example.test/item",
        "torrent",
        [],
        "hash",
        false,
        default,
        default,
        isDownloadFinished,
        null,
        null,
        null,
        null,
        null,
        null,
        isAiProcessed,
        isAiProcessed ? 0 : 1,
        MetadataStatus: isAiProcessed
            ? MetadataReviewStatus.Identified
            : MetadataReviewStatus.Pending);

    private static Incident CreateAiIncident(Guid sourceId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Incident(
            Guid.NewGuid(),
            "ai-fingerprint",
            IncidentType.AiFailure,
            IncidentSeverity.Error,
            "AI failed",
            "No result",
            sourceId.ToString(),
            now,
            now,
            null,
            1,
            now,
            null);
    }

    private static Incident CreateMappingIncident(Guid sourceId)
    {
        var now = DateTimeOffset.UtcNow;
        return new Incident(
            Guid.NewGuid(),
            "mapping-fingerprint",
            IncidentType.FileMappingFailure,
            IncidentSeverity.Error,
            "Mapping failed",
            "No mapping",
            sourceId.ToString(),
            now,
            now,
            null,
            1,
            now,
            null);
    }

    private sealed record ReconciliationFixture(
        IncidentReconciliationBackgroundService Service,
        Mock<IIncidentReporter> Reporter);
}
