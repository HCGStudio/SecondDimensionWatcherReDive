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
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<FileMapping>>(),
            It.IsAny<IReadOnlyList<FileMapping>>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
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
            fixture.Operation.Id, It.IsAny<IReadOnlyList<FileMapping>>(),
            It.IsAny<IReadOnlyList<FileMapping>>(),
            It.IsAny<DateTimeOffset>(),
            It.Is<DateTimeOffset>(until => until > DateTimeOffset.UtcNow.AddHours(71)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CandidateCompletedDuringClaim_ActivatesWithoutStartingAnotherDownload()
    {
        var fixture = new CoordinatorFixture(
            fileExists: true,
            candidateDownloaded: false,
            operationStatus: ReleaseUpgradeStatus.Verifying);

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        fixture.AnimationRepository.Verify(repository => repository.TryStartDownloadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<SubscriptionAutomationDisposition?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task CandidateCompletedBeforeMapping_KeepsVerifyingOperationPending()
    {
        var fixture = new CoordinatorFixture(
            fileExists: true,
            candidateDownloaded: false,
            operationStatus: ReleaseUpgradeStatus.Verifying,
            candidateMapped: false);

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("mapping_pending", result.Outcome);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExistingTrackedCandidate_IsFollowedWithoutReplacingItsAttempt()
    {
        var fixture = new CoordinatorFixture(
            fileExists: true,
            candidateDownloaded: false,
            candidateTracked: true);

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("download_in_progress", result.Outcome);
        fixture.AnimationRepository.Verify(repository => repository.TryStartDownloadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<SubscriptionAutomationDisposition?>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DownloadClientResolutionFailure_RestoresClaimedLocalState()
    {
        var fixture = new CoordinatorFixture(fileExists: true, candidateDownloaded: false);
        fixture.DownloadClientProvider
            .Setup(provider => provider.GetRequiredClient(fixture.CandidateInfo.DownloadType))
            .Throws(new InvalidOperationException("client unavailable"));

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        fixture.AnimationRepository.Verify(repository => repository.TryBeginCancelDownloadAsync(
            fixture.CandidateInfo.Id,
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            fixture.CandidateInfo.Id,
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            SubscriptionAutomationDisposition.AutoDownloadFailed,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.DownloadClient.Verify(client => client.CancelDownloadTaskAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            fixture.Operation.Id,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task DownloadSubmissionFailure_CancelsRemoteThenRestoresLocalState()
    {
        var fixture = new CoordinatorFixture(fileExists: true, candidateDownloaded: false);
        var cancellationRegistered = false;
        var remoteCancelled = false;
        fixture.AnimationRepository.Setup(repository => repository.TryBeginCancelDownloadAsync(
                fixture.CandidateInfo.Id,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => cancellationRegistered = true)
            .ReturnsAsync(true);
        fixture.DownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                fixture.CandidateInfo.Id,
                fixture.CandidateInfo.DownloadUrl,
                fixture.CandidateInfo.CachedDownloadData,
                fixture.CandidateInfo.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("submission interrupted"));
        fixture.DownloadClient.Setup(client => client.CancelDownloadTaskAsync(
                fixture.CandidateInfo.Id,
                fixture.CandidateInfo.DownloadUrl,
                fixture.CandidateInfo.CachedDownloadData,
                fixture.CandidateInfo.AdditionalDownloadInfo,
                false,
                It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Assert.IsTrue(cancellationRegistered);
                remoteCancelled = true;
            })
            .ReturnsAsync(new CancelDownloadResult(true, false));
        fixture.FileMappingRepository.Setup(repository => repository.TryFinalizeDownloadCancellationAsync(
                fixture.CandidateInfo.Id,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                SubscriptionAutomationDisposition.AutoDownloadFailed,
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.IsTrue(remoteCancelled))
            .ReturnsAsync(true);

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        fixture.DownloadClient.Verify(client => client.CancelDownloadTaskAsync(
            fixture.CandidateInfo.Id,
            fixture.CandidateInfo.DownloadUrl,
            fixture.CandidateInfo.CachedDownloadData,
            fixture.CandidateInfo.AdditionalDownloadInfo,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.AnimationRepository.Verify(repository => repository.TryBeginCancelDownloadAsync(
            fixture.CandidateInfo.Id,
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            fixture.CandidateInfo.Id,
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            SubscriptionAutomationDisposition.AutoDownloadFailed,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            fixture.Operation.Id,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task UnconfirmedRemoteCancellation_LeavesOperationRecoverable()
    {
        var fixture = new CoordinatorFixture(fileExists: true, candidateDownloaded: false);
        fixture.DownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                fixture.CandidateInfo.Id,
                fixture.CandidateInfo.DownloadUrl,
                fixture.CandidateInfo.CachedDownloadData,
                fixture.CandidateInfo.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("submission outcome unknown"));
        fixture.DownloadClient.Setup(client => client.CancelDownloadTaskAsync(
                fixture.CandidateInfo.Id,
                fixture.CandidateInfo.DownloadUrl,
                fixture.CandidateInfo.CachedDownloadData,
                fixture.CandidateInfo.AdditionalDownloadInfo,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CancelDownloadResult(false, false));

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("recovery_pending", result.Outcome);
        Assert.IsTrue(result.RequiresDownload);
        fixture.AnimationRepository.Verify(repository => repository.TryBeginCancelDownloadAsync(
            fixture.CandidateInfo.Id, It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid>(),
            It.IsAny<SubscriptionAutomationDisposition?>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task LocalCancellationConflict_LeavesOperationRecoverable()
    {
        var fixture = new CoordinatorFixture(fileExists: true, candidateDownloaded: false);
        fixture.DownloadClientProvider
            .Setup(provider => provider.GetRequiredClient(fixture.CandidateInfo.DownloadType))
            .Throws(new InvalidOperationException("client unavailable"));
        fixture.AnimationRepository.Setup(repository => repository.TryBeginCancelDownloadAsync(
                fixture.CandidateInfo.Id,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        fixture.AnimationRepository.SetupSequence(repository => repository.FindByIdAsync(
                fixture.CandidateInfo.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.CandidateInfo)
            .ReturnsAsync(fixture.CandidateInfo)
            .ReturnsAsync(fixture.CandidateInfo with { IsDownloadTracked = true });

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.AreEqual("recovery_pending", result.Outcome);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ActivationWinningBeforeCompensation_DoesNotDeleteRemoteCandidate()
    {
        var fixture = new CoordinatorFixture(fileExists: true, candidateDownloaded: false);
        fixture.DownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                fixture.CandidateInfo.Id,
                fixture.CandidateInfo.DownloadUrl,
                fixture.CandidateInfo.CachedDownloadData,
                fixture.CandidateInfo.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("submission outcome unknown"));
        fixture.AnimationRepository.Setup(repository => repository.TryBeginCancelDownloadAsync(
                fixture.CandidateInfo.Id,
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        fixture.AnimationRepository.SetupSequence(repository => repository.FindByIdAsync(
                fixture.CandidateInfo.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fixture.CandidateInfo)
            .ReturnsAsync(fixture.CandidateInfo)
            .ReturnsAsync(fixture.CandidateInfo with { IsDownloadTracked = true });

        var result = await fixture.Coordinator.ExecuteAsync(
            fixture.Candidate,
            dryRun: false,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("recovery_pending", result.Outcome);
        fixture.DownloadClient.Verify(client => client.CancelDownloadTaskAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMappingRepository.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid>(),
            It.IsAny<SubscriptionAutomationDisposition?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RequestCancellation_AfterCompensationTerminatesOperation()
    {
        var fixture = new CoordinatorFixture(fileExists: true, candidateDownloaded: false);
        fixture.DownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                fixture.CandidateInfo.Id,
                fixture.CandidateInfo.DownloadUrl,
                fixture.CandidateInfo.CachedDownloadData,
                fixture.CandidateInfo.AdditionalDownloadInfo,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Coordinator.ExecuteAsync(
                fixture.Candidate,
                dryRun: false,
                cancellation.Token));

        fixture.AnimationRepository.Verify(repository => repository.TryBeginCancelDownloadAsync(
            fixture.CandidateInfo.Id,
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.TryFinalizeDownloadCancellationAsync(
            fixture.CandidateInfo.Id,
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            SubscriptionAutomationDisposition.AutoDownloadFailed,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.UpgradeRepository.Verify(repository => repository.MarkFailedAsync(
            fixture.Operation.Id,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class CoordinatorFixture
    {
        public Mock<IReleaseUpgradeRepository> UpgradeRepository { get; } = new();
        public Mock<IAnimationInfoRepository> AnimationRepository { get; } = new();
        public Mock<IFileMappingRepository> FileMappingRepository { get; } = new();
        public Mock<IFileDownloadClient> DownloadClient { get; } = new();
        public Mock<IFileDownloadClientProvider> DownloadClientProvider { get; } = new();
        public Mock<IFileStore> FileStore { get; } = new();
        public Mock<IIncidentReporter> IncidentReporter { get; } = new();
        public ReleaseUpgradeOperation Operation { get; }
        public ReleaseUpgradeCandidate Candidate { get; }
        public AnimationInfo CandidateInfo { get; }
        public IReleaseUpgradeCoordinator Coordinator { get; }

        public CoordinatorFixture(
            bool fileExists,
            bool candidateDownloaded = true,
            bool? candidateTracked = null,
            ReleaseUpgradeStatus? operationStatus = null,
            bool candidateMapped = true)
        {
            var isCandidateTracked = candidateTracked ?? candidateDownloaded;
            Operation = new ReleaseUpgradeOperation(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                operationStatus ?? (candidateDownloaded
                    ? ReleaseUpgradeStatus.Verifying
                    : ReleaseUpgradeStatus.Downloading),
                200, 500,
                DateTimeOffset.UtcNow, null, null, null, null, null);
            Candidate = new ReleaseUpgradeCandidate(
                Operation.CurrentReleaseId,
                Operation.CandidateReleaseId,
                "candidate show",
                1,
                1,
                200,
                500,
                [],
                true);
            var previous = new FileMapping(
                Guid.NewGuid(), Operation.CurrentReleaseId, "/show/e01.mkv", "/store/old.mkv", "local");
            var candidate = new FileMapping(
                Guid.NewGuid(), Operation.CandidateReleaseId, "/show/e01 (2).mkv", "/store/new.mkv", "local");
            UpgradeRepository.Setup(repository => repository.FindActiveByCandidateAsync(
                    Operation.CandidateReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Operation);
            UpgradeRepository.Setup(repository => repository.GetActivationAsync(
                    Operation.CandidateReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReleaseUpgradeActivation(
                    Operation,
                    [previous],
                    candidateMapped ? [candidate] : []));
            UpgradeRepository.Setup(repository => repository.MarkFailedAsync(
                    Operation.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReleaseUpgradeMutationResult(true, "failed", Operation));
            UpgradeRepository.Setup(repository => repository.ActivateAsync(
                    Operation.Id, It.IsAny<IReadOnlyList<FileMapping>>(),
                    It.IsAny<IReadOnlyList<FileMapping>>(),
                    It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReleaseUpgradeMutationResult(true, "applied",
                    Operation with { Status = ReleaseUpgradeStatus.Applied }));
            UpgradeRepository.Setup(repository => repository.TryBeginAsync(
                    Candidate,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(Operation);

            FileStore.Setup(store => store.ExistAsync(
                    candidate.PhysicalPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(fileExists);
            FileStore.Setup(store => store.FileInfoAsync(
                    candidate.PhysicalPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileStoreInfo(false, candidate.PhysicalPath, "new.mkv", 1024));
            var storeProvider = new Mock<IFileStoreProvider>();
            storeProvider.Setup(provider => provider.GetRequiredClient("local"))
                .Returns(FileStore.Object);

            CandidateInfo = new AnimationInfo(
                Operation.CandidateReleaseId, "candidate", "", DateTimeOffset.UtcNow,
                "https://example.test/new", FileDownloadTypes.TorrentDownload, [], "",
                isCandidateTracked, default, default, candidateDownloaded,
                candidateDownloaded ? "local" : null,
                candidateDownloaded ? "/store/new" : null,
                1, 1, null, null, true, 0);
            AnimationRepository.Setup(repository => repository.FindByIdAsync(
                    Operation.CandidateReleaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CandidateInfo);
            AnimationRepository.Setup(repository => repository.TryStartDownloadAsync(
                    CandidateInfo.Id,
                    It.IsAny<Guid>(),
                    It.IsAny<DateTimeOffset>(),
                    SubscriptionAutomationDisposition.AutoDownloadQueued,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            AnimationRepository.Setup(repository => repository.TryBeginCancelDownloadAsync(
                    CandidateInfo.Id,
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            FileMappingRepository.Setup(repository => repository.TryFinalizeDownloadCancellationAsync(
                    CandidateInfo.Id,
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    SubscriptionAutomationDisposition.AutoDownloadFailed,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            DownloadClientProvider
                .Setup(provider => provider.GetRequiredClient(CandidateInfo.DownloadType))
                .Returns(DownloadClient.Object);
            DownloadClient.Setup(client => client.SubmitDownloadTaskAsync(
                    CandidateInfo.Id,
                    CandidateInfo.DownloadUrl,
                    CandidateInfo.CachedDownloadData,
                    CandidateInfo.AdditionalDownloadInfo,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            DownloadClient.Setup(client => client.CancelDownloadTaskAsync(
                    CandidateInfo.Id,
                    CandidateInfo.DownloadUrl,
                    CandidateInfo.CachedDownloadData,
                    CandidateInfo.AdditionalDownloadInfo,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CancelDownloadResult(true, false));

            Coordinator = new ReleaseUpgradeCoordinator(
                UpgradeRepository.Object,
                AnimationRepository.Object,
                Mock.Of<ISubscriptionAutomationPolicyRepository>(),
                FileMappingRepository.Object,
                DownloadClientProvider.Object,
                storeProvider.Object,
                IncidentReporter.Object,
                Mock.Of<ILogger<ReleaseUpgradeCoordinator>>());
        }
    }
}
