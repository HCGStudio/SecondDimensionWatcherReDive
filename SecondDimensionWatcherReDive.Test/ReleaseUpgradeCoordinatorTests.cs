using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.Incidents;
using SecondDimensionWatcherReDive.Utils.ReleaseUpgrades;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ReleaseUpgradeCoordinatorTests
{
    [TestMethod]
    public async Task CandidateValidationFailure_DoesNotInvokeAtomicSwap_AndRecordsFailure()
    {
        var fixture = new CoordinatorFixture(fileExists: false);

        var result = await fixture.Coordinator.TryActivateCandidateAsync(
            fixture.Operation.CandidateReleaseId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.IsSuccess);
        fixture.UpgradeRepository.Verify(repository => repository.ActivateAsync(
            It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            fixture.Operation.Id,
            It.Is<string>(summary => summary.Contains("missing", StringComparison.OrdinalIgnoreCase)),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.IncidentReporter.Verify(reporter => reporter.ReportAsync(
            It.Is<IncidentReport>(report => report.Title == "Release upgrade failed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ValidCandidate_InvokesAtomicSwapOnlyAfterEveryFilePasses()
    {
        var fixture = new CoordinatorFixture(fileExists: true);

        var result = await fixture.Coordinator.TryActivateCandidateAsync(
            fixture.Operation.CandidateReleaseId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsSuccess);
        fixture.FileStore.Verify(store => store.ExistAsync(
            "/store/new.mkv", It.IsAny<CancellationToken>()), Times.Once);
        fixture.FileStore.Verify(store => store.FileInfoAsync(
            "/store/new.mkv", It.IsAny<CancellationToken>()), Times.Once);
        fixture.UpgradeRepository.Verify(repository => repository.ActivateAsync(
            fixture.Operation.Id,
            It.IsAny<DateTimeOffset>(),
            It.Is<DateTimeOffset>(until => until > DateTimeOffset.UtcNow.AddHours(71)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class CoordinatorFixture
    {
        public Mock<IReleaseUpgradeRepository> UpgradeRepository { get; } = new();
        public Mock<IFileStore> FileStore { get; } = new();
        public Mock<IIncidentReporter> IncidentReporter { get; } = new();
        public ReleaseUpgradeOperation Operation { get; }
        public IReleaseUpgradeCoordinator Coordinator { get; }

        public CoordinatorFixture(bool fileExists)
        {
            Operation = new ReleaseUpgradeOperation(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                ReleaseUpgradeStatus.Verifying, 200, 500,
                DateTimeOffset.UtcNow, null, null, null, null, null);
            var previous = new FileMapping(
                Guid.NewGuid(), Operation.CurrentReleaseId, "/show/e01.mkv", "/store/old.mkv", "local");
            var candidate = new FileMapping(
                Guid.NewGuid(), Operation.CandidateReleaseId, "/show/e01 (2).mkv", "/store/new.mkv", "local");
            UpgradeRepository.Setup(repository => repository.FindActiveByCandidateAsync(
                    Operation.CandidateReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Operation);
            UpgradeRepository.Setup(repository => repository.GetActivationAsync(
                    Operation.CandidateReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReleaseUpgradeActivation(Operation, [previous], [candidate]));
            UpgradeRepository.Setup(repository => repository.MarkFailedAsync(
                    Operation.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReleaseUpgradeMutationResult(true, "failed", Operation));
            UpgradeRepository.Setup(repository => repository.ActivateAsync(
                    Operation.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReleaseUpgradeMutationResult(true, "applied",
                    Operation with { Status = ReleaseUpgradeStatus.Applied }));

            FileStore.Setup(store => store.ExistAsync(
                    candidate.PhysicalPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(fileExists);
            FileStore.Setup(store => store.FileInfoAsync(
                    candidate.PhysicalPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileStoreInfo(false, candidate.PhysicalPath, "new.mkv", 1024));
            var storeProvider = new Mock<IFileStoreProvider>();
            storeProvider.Setup(provider => provider.GetRequiredClient("local"))
                .Returns(FileStore.Object);

            var animationRepository = new Mock<IAnimationInfoRepository>();
            animationRepository.Setup(repository => repository.FindByIdAsync(
                    Operation.CandidateReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnimationInfo(
                    Operation.CandidateReleaseId, "candidate", "", DateTimeOffset.UtcNow,
                    "https://example.test/new", FileDownloadTypes.TorrentDownload, [], "",
                    true, default, default, true, "local", "/store/new", 1, 1,
                    null, null, true, 0));

            Coordinator = new ReleaseUpgradeCoordinator(
                UpgradeRepository.Object,
                animationRepository.Object,
                Mock.Of<ISubscriptionAutomationPolicyRepository>(),
                Mock.Of<IFileMappingRepository>(),
                Mock.Of<IFileDownloadClientProvider>(),
                storeProvider.Object,
                IncidentReporter.Object,
                Mock.Of<ILogger<ReleaseUpgradeCoordinator>>());
        }
    }
}
