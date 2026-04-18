using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Plugin.FileRenamer;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class VideoFileRenamerTests
{
    private Mock<IFileStore> _fileStoreMock = null!;
    private Mock<IFileOperator> _fileOperatorMock = null!;
    private Mock<IAnimationInfoRepository> _animationInfoRepoMock = null!;
    private Mock<ILogger<VideoFileRenamer>> _loggerMock = null!;

    private static readonly Animation TestAnimation = new(
        Guid.NewGuid(), "12345", "My Anime", "My Anime Original", "/poster.jpg");

    [TestInitialize]
    public void Setup()
    {
        _fileStoreMock = new Mock<IFileStore>();
        _fileOperatorMock = new Mock<IFileOperator>();
        _animationInfoRepoMock = new Mock<IAnimationInfoRepository>();
        _loggerMock = new Mock<ILogger<VideoFileRenamer>>();
    }

    private VideoFileRenamer CreateRenamer(IInferenceEngine? inferenceEngine = null) =>
        new(_fileStoreMock.Object, _fileOperatorMock.Object, _animationInfoRepoMock.Object,
            _loggerMock.Object, inferenceEngine);

    private static AnimationInfo CreateTestInfo(
        string storePath, int? season = 1, int? episode = 5, Animation? animation = null) =>
        new(Guid.NewGuid(), "Test Title", "", DateTimeOffset.Now,
            "", "", Array.Empty<byte>(), "",
            false, default, default, true,
            "local", storePath, season, episode,
            null, animation ?? TestAnimation, false, 0);

    [TestMethod]
    public async Task RenameAsync_SingleEpisode_FormatsCorrectly()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(true, storePath, "anime"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.mkv", "[Group] Title - 05.mkv")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = CreateRenamer();
        var info = CreateTestInfo(storePath);
        var request = new FileRenameRequest("My Anime", 1, 5, storePath, info);

        await renamer.RenameAsync(request, CancellationToken.None);

        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.mkv",
                It.Is<string>(s => Regex.IsMatch(s, @"^/downloads/anime/My Anime S01E05 \[[a-z0-9]{5}\]\.mkv$")),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task RenameAsync_SingleEpisode_RenamesMatchingSubtitles()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(true, storePath, "anime"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(
                new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.mkv", "[Group] Title - 05.mkv"),
                new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.srt", "[Group] Title - 05.srt"),
                new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.zh.ass", "[Group] Title - 05.zh.ass"),
                new FileStoreInfo(false, "/downloads/anime/unrelated.srt", "unrelated.srt")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = CreateRenamer();
        var info = CreateTestInfo(storePath);
        var request = new FileRenameRequest("My Anime", 1, 5, storePath, info);

        await renamer.RenameAsync(request, CancellationToken.None);

        // Video renamed
        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.mkv",
                It.Is<string>(s => Regex.IsMatch(s, @"^/downloads/anime/My Anime S01E05 \[[a-z0-9]{5}\]\.mkv$")),
                CancellationToken.None),
            Times.Once);
        // Plain subtitle renamed
        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.srt",
                It.Is<string>(s => Regex.IsMatch(s, @"^/downloads/anime/My Anime S01E05 \[[a-z0-9]{5}\]\.srt$")),
                CancellationToken.None),
            Times.Once);
        // Subtitle with language tag renamed, tag preserved
        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.zh.ass",
                It.Is<string>(s => Regex.IsMatch(s, @"^/downloads/anime/My Anime S01E05 \[[a-z0-9]{5}\]\.zh\.ass$")),
                CancellationToken.None),
            Times.Once);
        // Unrelated subtitle NOT renamed (3 total renames: 1 video + 2 subtitles)
        _fileOperatorMock.Verify(
            o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [TestMethod]
    public async Task RenameAsync_NoVideoFiles_DoesNotRename()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(true, storePath, "anime"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/readme.txt", "readme.txt")));

        var renamer = CreateRenamer();
        var info = CreateTestInfo(storePath);
        var request = new FileRenameRequest("My Anime", 1, 5, storePath, info);

        await renamer.RenameAsync(request, CancellationToken.None);

        _fileOperatorMock.Verify(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_StorePathNotExist_ReturnsEarly()
    {
        _fileStoreMock.Setup(s => s.ExistAsync("/missing", CancellationToken.None)).ReturnsAsync(false);

        var renamer = CreateRenamer();
        var info = CreateTestInfo("/missing");
        var request = new FileRenameRequest("My Anime", 1, 5, "/missing", info);

        await renamer.RenameAsync(request, CancellationToken.None);

        _fileStoreMock.Verify(s => s.EnumerateDirectory(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_SanitizesInvalidChars()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(true, storePath, "anime"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/video.mp4", "video.mp4")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = CreateRenamer();
        var info = CreateTestInfo(storePath, season: 2, episode: 3);
        // Name with null char which is universally invalid
        var request = new FileRenameRequest("Anime\0Title", 2, 3, storePath, info);

        await renamer.RenameAsync(request, CancellationToken.None);

        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/video.mp4",
                It.Is<string>(s => Regex.IsMatch(s, @"S02E03 \[[a-z0-9]{5}\]\.mp4$") && !s.Contains("\0")),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task RenameMultipleAsync_NoInference_LogsWarning()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(true, storePath, "anime"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(
                new FileStoreInfo(false, "/downloads/anime/ep1.mkv", "ep1.mkv"),
                new FileStoreInfo(false, "/downloads/anime/ep2.mkv", "ep2.mkv")));

        // No inference engine
        var renamer = CreateRenamer();
        var request = new MultipleFileRenameRequest("My Anime", 1, "Title", storePath);

        await renamer.RenameMultipleAsync(request, CancellationToken.None);

        // Should not rename since there's no inference engine
        _fileOperatorMock.Verify(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_DirectoryBackedSingleEpisode_DoesNotUpdateStorePath()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(true, storePath, "anime"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/video.mkv", "video.mkv")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = CreateRenamer();
        var info = CreateTestInfo(storePath);
        var request = new FileRenameRequest("My Anime", 1, 5, storePath, info);

        await renamer.RenameAsync(request, CancellationToken.None);

        _fileOperatorMock.Verify(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _animationInfoRepoMock.Verify(r => r.UpdateAsync(It.IsAny<AnimationInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_FileBackedSingleEpisode_UpdatesStorePath()
    {
        var storePath = "/downloads/[Group] Title - 05.mkv";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.FileInfoAsync(storePath, CancellationToken.None))
            .ReturnsAsync(new FileStoreInfo(false, storePath, "[Group] Title - 05.mkv"));
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, storePath, "[Group] Title - 05.mkv")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = CreateRenamer();
        var info = CreateTestInfo(storePath);
        var request = new FileRenameRequest("My Anime", 1, 5, storePath, info);

        await renamer.RenameAsync(request, CancellationToken.None);

        _animationInfoRepoMock.Verify(
            r => r.UpdateAsync(
                It.Is<AnimationInfo>(i => i.StorePath != null && Regex.IsMatch(i.StorePath, @"My Anime S01E05 \[[a-z0-9]{5}\]\.mkv$")),
                CancellationToken.None),
            Times.Once);
    }

    private static async IAsyncEnumerable<FileStoreInfo> ToAsyncEnumerable(params FileStoreInfo[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
