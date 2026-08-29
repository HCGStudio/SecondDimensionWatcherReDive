using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using DataAnimationInfo = SecondDimensionWatcherReDive.Framework.DataRepository.AnimationInfo;
using DataAnimation = SecondDimensionWatcherReDive.Framework.DataRepository.Animation;
using DataAnimationGroup = SecondDimensionWatcherReDive.Framework.DataRepository.AnimationGroup;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class PlaybackControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private Mock<IPlaybackRepository> _playbackRepository = null!;
    private Mock<IAnimationInfoRepository> _animationInfoRepository = null!;
    private Mock<IFileMappingRepository> _fileMappingRepository = null!;
    private PlaybackController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _playbackRepository = new Mock<IPlaybackRepository>();
        _animationInfoRepository = new Mock<IAnimationInfoRepository>();
        _fileMappingRepository = new Mock<IFileMappingRepository>();
        _controller = new PlaybackController(
            _playbackRepository.Object,
            _animationInfoRepository.Object,
            _fileMappingRepository.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(IdentityClaimTypes.ProfileId, UserId.ToString())],
                        "test"))
                }
            }
        };
    }

    [TestMethod]
    public async Task UpdateProgress_PathTraversal_ReturnsBadRequestBeforeRepositoryAccess()
    {
        var request = new PlaybackProgressRequest(Guid.NewGuid(), "../secret.mkv", 10, 100);

        var result = await _controller.UpdateProgress(request, CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestResult>(result);
        _animationInfoRepository.Verify(
            repository => repository.FindByIdWithAnimationAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _playbackRepository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task UpdateProgress_AtNinetyPercent_AutomaticallyMarksWatched()
    {
        var info = CreateInfo();
        var relativePath = "Show S01E02.mkv";
        var virtualPath = $"/A Show/Sub Group/{relativePath}";
        var mapping = CreateMapping(info.Id, virtualPath);
        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(info.Id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMappingRepository
            .Setup(repository => repository.FindByVirtualPathAsync(virtualPath, CancellationToken.None))
            .ReturnsAsync(mapping);
        _playbackRepository
            .Setup(repository => repository.UpsertProgressAsync(
                UserId,
                info.Id,
                virtualPath,
                90,
                100,
                true,
                It.IsAny<DateTimeOffset>(),
                CancellationToken.None))
            .ReturnsAsync((Guid userId, Guid animationInfoId, string path, double position,
                    double duration, bool watched, DateTimeOffset now, CancellationToken _) =>
                new PlaybackProgress(
                    Guid.NewGuid(), userId, animationInfoId, path, position, duration, watched, now, now));

        var result = await _controller.UpdateProgress(
            new PlaybackProgressRequest(info.Id, relativePath, 90, 100),
            CancellationToken.None);

        var response = Assert.IsInstanceOfType<OkObjectResult>(result);
        var state = Assert.IsInstanceOfType<PlaybackStateResponse>(response.Value);
        Assert.IsTrue(state.IsWatched);
        Assert.AreEqual(relativePath, state.Path);
        Assert.AreEqual(virtualPath, state.VirtualPath);
    }

    [TestMethod]
    public async Task UpdateProgress_MappingOwnedByAnotherDownload_ReturnsNotFound()
    {
        var info = CreateInfo();
        const string relativePath = "Show S01E02.mkv";
        var virtualPath = $"/A Show/Sub Group/{relativePath}";
        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(info.Id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMappingRepository
            .Setup(repository => repository.FindByVirtualPathAsync(virtualPath, CancellationToken.None))
            .ReturnsAsync(CreateMapping(Guid.NewGuid(), virtualPath));

        var result = await _controller.UpdateProgress(
            new PlaybackProgressRequest(info.Id, relativePath, 1, 100),
            CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
        _playbackRepository.Verify(
            repository => repository.UpsertProgressAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task GetContext_AssociatesOnlyMatchingSidecarsAndReturnsNextRelativePath()
    {
        var info = CreateInfo();
        const string relativePath = "Show S01E02.mkv";
        var virtualPath = $"/A Show/Sub Group/{relativePath}";
        var video = CreateMapping(info.Id, virtualPath);
        var matchingSubtitle = CreateMapping(info.Id, "/A Show/Sub Group/Show S01E02.zh-Hans.ass");
        var otherVideo = CreateMapping(info.Id, "/A Show/Sub Group/Show S01E03.mkv");
        var otherSubtitle = CreateMapping(info.Id, "/A Show/Sub Group/Show S01E03.en.srt");
        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(info.Id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMappingRepository
            .Setup(repository => repository.FindByVirtualPathAsync(virtualPath, CancellationToken.None))
            .ReturnsAsync(video);
        _fileMappingRepository
            .Setup(repository => repository.GetForAnimationInfoAsync(info.Id, CancellationToken.None))
            .ReturnsAsync([video, matchingSubtitle, otherVideo, otherSubtitle]);
        _playbackRepository
            .Setup(repository => repository.FindProgressAsync(
                UserId, info.Id, virtualPath, CancellationToken.None))
            .ReturnsAsync((PlaybackProgress?)null);
        _playbackRepository
            .Setup(repository => repository.GetPreferencesAsync(UserId, CancellationToken.None))
            .ReturnsAsync(new PlaybackPreferences(
                UserId, "zh-Hans", "简体", "ja", "Japanese", true, DateTimeOffset.UnixEpoch));
        _playbackRepository
            .Setup(repository => repository.GetNextMediaAsync(
                info.Id, virtualPath, CancellationToken.None))
            .ReturnsAsync(new PlaybackMedia(
                Guid.NewGuid(),
                "/A Show/Sub Group/Show S01E03.mkv",
                "Show S01E03.mkv",
                "Episode 3",
                info.Animation!.Id,
                info.Animation.Name,
                info.Animation.PosterPath,
                info.Group!.Id,
                info.Group.Name,
                1,
                3,
                DateTimeOffset.UtcNow));

        var result = await _controller.GetContext(info.Id, relativePath, CancellationToken.None);

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result);
        var context = Assert.IsInstanceOfType<PlaybackContextResponse>(ok.Value);
        Assert.AreEqual(relativePath, context.Media.Path);
        Assert.IsNull(context.State);
        Assert.AreEqual("Show S01E03.mkv", context.Next!.Path);
        Assert.HasCount(1, context.Subtitles);
        Assert.AreEqual("Show S01E02.zh-Hans.ass", context.Subtitles[0].Path);
        Assert.AreEqual("zh-Hans", context.Subtitles[0].Language);
    }

    [TestMethod]
    public async Task UpdatePreferences_TrimsTrackLabelsAndPersistsNullForBlankValues()
    {
        PlaybackPreferences? captured = null;
        _playbackRepository
            .Setup(repository => repository.UpsertPreferencesAsync(
                It.IsAny<PlaybackPreferences>(), CancellationToken.None))
            .Callback<PlaybackPreferences, CancellationToken>((preferences, _) => captured = preferences)
            .ReturnsAsync((PlaybackPreferences preferences, CancellationToken _) => preferences);

        var result = await _controller.UpdatePreferences(
            new PlaybackPreferencesRequest(" zh-Hans ", " 简体中文 ", "  ", " Japanese ", false),
            CancellationToken.None);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsNotNull(captured);
        Assert.AreEqual(UserId, captured.UserId);
        Assert.AreEqual("zh-Hans", captured.SubtitleLanguage);
        Assert.AreEqual("简体中文", captured.SubtitleTrackLabel);
        Assert.IsNull(captured.AudioLanguage);
        Assert.AreEqual("Japanese", captured.AudioTrackLabel);
        Assert.IsFalse(captured.AutoPlayNext);
    }

    [TestMethod]
    public async Task GetStates_ReturnsEveryVideoAndFiltersStaleAndNonVideoProgress()
    {
        var info = CreateInfo();
        var video = CreateMapping(info.Id, "/A Show/Sub Group/Show S01E02.mkv");
        var unplayedVideo = CreateMapping(info.Id, "/A Show/Sub Group/Show S01E03.mkv");
        var unaddressableVideo = CreateMapping(info.Id, "/unknown/bonus.mkv");
        var subtitle = CreateMapping(info.Id, "/A Show/Sub Group/Show S01E02.ass");
        _animationInfoRepository
            .Setup(repository => repository.FindByIdWithAnimationAsync(info.Id, CancellationToken.None))
            .ReturnsAsync(info);
        _fileMappingRepository
            .Setup(repository => repository.GetForAnimationInfoAsync(info.Id, CancellationToken.None))
            .ReturnsAsync([video, unplayedVideo, unaddressableVideo, subtitle]);
        _playbackRepository
            .Setup(repository => repository.GetStatesAsync(UserId, info.Id, CancellationToken.None))
            .ReturnsAsync([
                CreateProgress(info.Id, video.VirtualPath),
                CreateProgress(info.Id, subtitle.VirtualPath),
                CreateProgress(info.Id, "/stale.mkv")
            ]);

        var result = await _controller.GetStates(info.Id, CancellationToken.None);

        var ok = Assert.IsInstanceOfType<OkObjectResult>(result);
        var states = Assert.IsInstanceOfType<PlaybackStateResponse[]>(ok.Value);
        Assert.HasCount(2, states);
        Assert.AreEqual("Show S01E02.mkv", states[0].Path);
        Assert.AreEqual("Show S01E03.mkv", states[1].Path);
        Assert.AreEqual(0, states[1].PositionSeconds);
        Assert.IsFalse(states[1].IsWatched);
        Assert.IsNull(states[1].UpdatedAt);
    }

    private static DataAnimationInfo CreateInfo()
    {
        var animation = new DataAnimation(Guid.NewGuid(), "42", "A Show", "A Show", "/poster.jpg");
        var group = new DataAnimationGroup(Guid.NewGuid(), "Sub Group");
        return new DataAnimationInfo(
            Guid.NewGuid(),
            "Episode 2",
            "Description",
            DateTimeOffset.UtcNow,
            "https://example.test/torrent",
            "torrent",
            [],
            "hash",
            true,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            true,
            "Local",
            "/downloads/show",
            1,
            2,
            group,
            animation,
            true,
            0);
    }

    private static FileMapping CreateMapping(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), animationInfoId, virtualPath, "/physical/file", "Local");

    private static PlaybackProgress CreateProgress(Guid animationInfoId, string virtualPath) =>
        new(Guid.NewGuid(), UserId, animationInfoId, virtualPath, 10, 100, false,
            DateTimeOffset.UtcNow, null);
}
