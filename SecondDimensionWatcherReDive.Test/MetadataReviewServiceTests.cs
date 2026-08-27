using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Inference.AI.Tools;
using SecondDimensionWatcherReDive.Utils.FileStore;
using SecondDimensionWatcherReDive.Utils.MetadataReview;
using TMDbLib.Client;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class MetadataReviewServiceTests
{
    private Mock<IAnimationInfoRepository> _animationInfoRepository = null!;
    private Mock<IAnimationRepository> _animationRepository = null!;
    private Mock<IAnimationGroupRepository> _animationGroupRepository = null!;
    private Mock<IFileMappingRepository> _fileMappingRepository = null!;
    private Mock<IMetadataReviewRepository> _metadataReviewRepository = null!;
    private Mock<IFileMapper> _fileMapper = null!;
    private MetadataReviewService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _animationInfoRepository = new Mock<IAnimationInfoRepository>();
        _animationRepository = new Mock<IAnimationRepository>();
        _animationGroupRepository = new Mock<IAnimationGroupRepository>();
        _fileMappingRepository = new Mock<IFileMappingRepository>();
        _metadataReviewRepository = new Mock<IMetadataReviewRepository>();
        _fileMapper = new Mock<IFileMapper>();

        var tmdbClient = new TMDbClient("fake-key", false, "127.0.0.1:1");
        var tmdbTool = new TmdbTool(tmdbClient, Mock.Of<ILogger<TmdbTool>>());
        _service = new MetadataReviewService(
            _animationInfoRepository.Object,
            _animationRepository.Object,
            _animationGroupRepository.Object,
            _fileMappingRepository.Object,
            _metadataReviewRepository.Object,
            _fileMapper.Object,
            tmdbTool);
    }

    [TestMethod]
    public async Task PreviewAsync_InvalidCorrection_ReportsSpecificValidationCode()
    {
        var item = CreateInfo(stateVersion: 5);
        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(
                item.Id,
                CancellationToken.None))
            .ReturnsAsync(item);
        var cases = new (MetadataReviewCorrection Correction, string Code)[]
        {
            (new MetadataReviewCorrection("not-a-number", 1, 1, null), "invalidTmdbId"),
            (new MetadataReviewCorrection("123", null, 1, null), "seasonRequired"),
            (new MetadataReviewCorrection("123", -1, 1, null), "invalidSeason"),
            (new MetadataReviewCorrection("123", 1, -1, null), "invalidEpisode"),
            (new MetadataReviewCorrection("123", 1, 1, new string('x', 201)), "groupNameTooLong")
        };

        foreach (var (correction, expectedCode) in cases)
        {
            try
            {
                await _service.PreviewAsync(
                    item.Id,
                    item.StateVersion,
                    correction,
                    CancellationToken.None);
                Assert.Fail($"Expected validation error {expectedCode}.");
            }
            catch (MetadataReviewValidationException exception)
            {
                Assert.AreEqual(expectedCode, exception.Code);
            }
        }

        _animationRepository.Verify(repository => repository.FindByTmdbIdAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _fileMapper.Verify(mapper => mapper.PreviewDownloadAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PreviewAsync_StaleRevision_RejectsBeforeResolvingMetadata()
    {
        var item = CreateInfo(stateVersion: 8);
        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(
                item.Id,
                CancellationToken.None))
            .ReturnsAsync(item);

        var exception = await Assert.ThrowsExactlyAsync<MetadataReviewConflictException>(() =>
            _service.PreviewAsync(
                item.Id,
                expectedRevision: 7,
                new MetadataReviewCorrection("123", 1, 1, "Group"),
                CancellationToken.None));

        Assert.AreEqual("staleRevision", exception.Code);
        _animationRepository.Verify(repository => repository.FindByTmdbIdAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _fileMapper.Verify(mapper => mapper.PreviewDownloadAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _metadataReviewRepository.Verify(repository => repository.SavePreviewAsync(
            It.IsAny<MetadataReviewPreviewDraft>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task PreviewAsync_DownloadedItem_PlansPathsAndPersistsVersionedDraft()
    {
        var item = CreateInfo(stateVersion: 12, isDownloadFinished: true);
        var targetAnimation = new Animation(
            Guid.NewGuid(),
            "456",
            "Target Anime",
            "Target Original",
            "/target.jpg");
        var targetGroup = new AnimationGroup(Guid.NewGuid(), "Target Group");
        var currentMapping = new FileMapping(
            Guid.NewGuid(),
            item.Id,
            "/unknown/Anime - 03.mkv",
            "/downloads/anime/Anime - 03.mkv",
            "test-store");
        var proposedMapping = currentMapping with
        {
            Id = Guid.NewGuid(),
            VirtualPath = "/Target Anime/Target Group/Target Anime S02E03 (2).mkv"
        };
        AnimationInfo? plannedInfo = null;
        MetadataReviewPreviewDraft? savedDraft = null;

        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(
                item.Id,
                CancellationToken.None))
            .ReturnsAsync(item);
        _animationRepository
            .Setup(repository => repository.FindByTmdbIdAsync(
                "456",
                CancellationToken.None))
            .ReturnsAsync(targetAnimation);
        _animationGroupRepository
            .Setup(repository => repository.FindByNameAsync(
                "Target Group",
                CancellationToken.None))
            .ReturnsAsync(targetGroup);
        _fileMappingRepository
            .Setup(repository => repository.GetForAnimationInfoAsync(
                item.Id,
                CancellationToken.None))
            .ReturnsAsync([currentMapping]);
        _fileMapper
            .Setup(mapper => mapper.PreviewDownloadAsync(
                It.IsAny<AnimationInfo>(),
                CancellationToken.None))
            .Callback<AnimationInfo, CancellationToken>((info, _) => plannedInfo = info)
            .ReturnsAsync(new FileMappingPreview(
                [proposedMapping],
                ["collisionAdjusted"]));
        _metadataReviewRepository
            .Setup(repository => repository.SavePreviewAsync(
                It.IsAny<MetadataReviewPreviewDraft>(),
                CancellationToken.None))
            .Callback<MetadataReviewPreviewDraft, CancellationToken>((draft, _) => savedDraft = draft)
            .Returns(Task.CompletedTask);

        var result = await _service.PreviewAsync(
            item.Id,
            item.StateVersion,
            new MetadataReviewCorrection(" 456 ", 2, 3, " Target Group "),
            CancellationToken.None);

        Assert.IsNotNull(plannedInfo);
        Assert.AreEqual(targetAnimation, plannedInfo.Animation);
        Assert.AreEqual(targetGroup, plannedInfo.Group);
        Assert.AreEqual(2, plannedInfo.Season);
        Assert.AreEqual(3, plannedInfo.Episode);
        Assert.AreEqual(MetadataReviewStatus.Reviewed, plannedInfo.MetadataStatus);
        Assert.AreEqual(1d, plannedInfo.MetadataConfidence);
        Assert.AreEqual(item.StateVersion, result.BaseRevision);
        Assert.IsTrue(result.CanApply);
        CollectionAssert.Contains(result.Warnings.ToList(), "collisionAdjusted");
        Assert.AreEqual(1, result.PathChanges.Count);
        Assert.AreEqual("moved", result.PathChanges[0].ChangeKind);
        Assert.IsTrue(result.PathChanges[0].CollisionAdjusted);

        Assert.IsNotNull(savedDraft);
        Assert.AreEqual(result.PreviewId, savedDraft.Id);
        Assert.AreEqual(item.StateVersion, savedDraft.BaseVersion);
        Assert.AreEqual(item.FileStore, savedDraft.BaseFileStore);
        Assert.AreEqual(item.StorePath, savedDraft.BaseStorePath);
        Assert.AreEqual(targetAnimation, savedDraft.ProposedAnimation);
        Assert.AreEqual("Target Group", savedDraft.ProposedGroupName);
        Assert.AreEqual(proposedMapping, savedDraft.ProposedMappings.Single());
    }

    [TestMethod]
    public async Task ApplyAsync_Success_ReturnsRevisionAndPathDiff()
    {
        var operationId = Guid.NewGuid();
        var animationInfoId = Guid.NewGuid();
        var appliedAt = DateTimeOffset.UtcNow;
        var before = new FileMapping(
            Guid.NewGuid(),
            animationInfoId,
            "/unknown/episode.mkv",
            "/store/episode.mkv",
            "test-store");
        var after = before with
        {
            Id = Guid.NewGuid(),
            VirtualPath = "/Anime/Group/Anime S01E01.mkv"
        };
        _metadataReviewRepository
            .Setup(repository => repository.ApplyPreviewAsync(
                operationId,
                animationInfoId,
                CancellationToken.None))
            .ReturnsAsync(new MetadataReviewMutationResult(
                MetadataReviewMutationOutcome.Success,
                operationId,
                animationInfoId,
                14,
                appliedAt,
                [before],
                [after]));

        var result = await _service.ApplyAsync(
            animationInfoId,
            operationId,
            CancellationToken.None);

        Assert.AreEqual(operationId, result.OperationId);
        Assert.AreEqual(14, result.Revision);
        Assert.AreEqual(appliedAt, result.AppliedAt);
        Assert.IsTrue(result.CanUndo);
        Assert.AreEqual(1, result.PathChanges.Count);
        Assert.AreEqual("moved", result.PathChanges[0].ChangeKind);
        Assert.AreEqual(before.VirtualPath, result.PathChanges[0].CurrentVirtualPath);
        Assert.AreEqual(after.VirtualPath, result.PathChanges[0].ProposedVirtualPath);
    }

    [TestMethod]
    [DataRow(MetadataReviewMutationOutcome.NotFound, "operationNotFound")]
    [DataRow(MetadataReviewMutationOutcome.Expired, "previewExpired")]
    [DataRow(MetadataReviewMutationOutcome.Conflict, "stalePreview")]
    public async Task ApplyAsync_NonSuccessOutcome_ThrowsCodedServiceError(
        MetadataReviewMutationOutcome outcome,
        string expectedCode)
    {
        var operationId = Guid.NewGuid();
        var animationInfoId = Guid.NewGuid();
        _metadataReviewRepository
            .Setup(repository => repository.ApplyPreviewAsync(
                operationId,
                animationInfoId,
                CancellationToken.None))
            .ReturnsAsync(new MetadataReviewMutationResult(
                outcome,
                operationId,
                animationInfoId,
                null,
                null,
                [],
                []));

        MetadataReviewServiceException? exception = null;
        try
        {
            await _service.ApplyAsync(
                animationInfoId,
                operationId,
                CancellationToken.None);
        }
        catch (MetadataReviewServiceException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception);
        Assert.AreEqual(expectedCode, exception.Code);
    }

    [TestMethod]
    public async Task UndoAsync_Success_ReturnsRestoredPathsAndCannotUndoAgain()
    {
        var operationId = Guid.NewGuid();
        var animationInfoId = Guid.NewGuid();
        var undoneAt = DateTimeOffset.UtcNow;
        var applied = new FileMapping(
            Guid.NewGuid(),
            animationInfoId,
            "/Anime/Group/Anime S01E01.mkv",
            "/store/episode.mkv",
            "test-store");
        var restored = applied with
        {
            Id = Guid.NewGuid(),
            VirtualPath = "/unknown/episode.mkv"
        };
        _metadataReviewRepository
            .Setup(repository => repository.UndoAsync(
                operationId,
                14,
                CancellationToken.None))
            .ReturnsAsync(new MetadataReviewMutationResult(
                MetadataReviewMutationOutcome.Success,
                operationId,
                animationInfoId,
                15,
                undoneAt,
                [applied],
                [restored]));

        var result = await _service.UndoAsync(
            operationId,
            expectedRevision: 14,
            CancellationToken.None);

        Assert.AreEqual(15, result.Revision);
        Assert.IsFalse(result.CanUndo);
        Assert.AreEqual("moved", result.PathChanges.Single().ChangeKind);
        Assert.AreEqual(applied.VirtualPath, result.PathChanges.Single().CurrentVirtualPath);
        Assert.AreEqual(restored.VirtualPath, result.PathChanges.Single().ProposedVirtualPath);
    }

    [TestMethod]
    public async Task UndoAsync_Conflict_ReportsUndoConflict()
    {
        var operationId = Guid.NewGuid();
        _metadataReviewRepository
            .Setup(repository => repository.UndoAsync(
                operationId,
                14,
                CancellationToken.None))
            .ReturnsAsync(new MetadataReviewMutationResult(
                MetadataReviewMutationOutcome.Conflict,
                operationId,
                null,
                null,
                null,
                [],
                []));

        var exception = await Assert.ThrowsExactlyAsync<MetadataReviewConflictException>(() =>
            _service.UndoAsync(
                operationId,
                expectedRevision: 14,
                CancellationToken.None));

        Assert.AreEqual("undoConflict", exception.Code);
    }

    private static AnimationInfo CreateInfo(
        long stateVersion,
        bool isDownloadFinished = false)
    {
        var currentAnimation = new Animation(
            Guid.NewGuid(),
            "123",
            "Current Anime",
            "Current Original",
            null);
        return new AnimationInfo(
            Guid.NewGuid(),
            "Release title",
            "Current description",
            DateTimeOffset.UtcNow,
            "https://example.test/release.torrent",
            "torrent",
            [],
            "hash",
            isDownloadFinished,
            default,
            default,
            isDownloadFinished,
            isDownloadFinished ? "test-store" : null,
            isDownloadFinished ? "/downloads/anime" : null,
            1,
            1,
            new AnimationGroup(Guid.NewGuid(), "Current Group"),
            currentAnimation,
            true,
            0,
            MetadataStatus: MetadataReviewStatus.LowConfidence,
            MetadataConfidence: 0.4,
            StateVersion: stateVersion);
    }
}
