using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Plugin.FileRenamer;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class VideoFileRenamerTests
{
    private Mock<IFileStore> _fileStoreMock = null!;
    private Mock<IFileOperator> _fileOperatorMock = null!;
    private Mock<ILogger<VideoFileRenamer>> _loggerMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _fileStoreMock = new Mock<IFileStore>();
        _fileOperatorMock = new Mock<IFileOperator>();
        _loggerMock = new Mock<ILogger<VideoFileRenamer>>();
    }

    [TestMethod]
    public async Task RenameAsync_SingleEpisode_FormatsCorrectly()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.mkv", "[Group] Title - 05.mkv")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "[Group] Title - 05", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.mkv",
                "/downloads/anime/My Anime S01E05.mkv",
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task RenameAsync_SingleEpisode_RenamesMatchingSubtitles()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(
                new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.mkv", "[Group] Title - 05.mkv"),
                new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.srt", "[Group] Title - 05.srt"),
                new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.zh.ass", "[Group] Title - 05.zh.ass"),
                new FileStoreInfo(false, "/downloads/anime/unrelated.srt", "unrelated.srt")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "[Group] Title - 05", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        // Video renamed
        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.mkv",
                "/downloads/anime/My Anime S01E05.mkv",
                CancellationToken.None),
            Times.Once);
        // Plain subtitle renamed
        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.srt",
                "/downloads/anime/My Anime S01E05.srt",
                CancellationToken.None),
            Times.Once);
        // Subtitle with language tag renamed, tag preserved
        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/[Group] Title - 05.zh.ass",
                "/downloads/anime/My Anime S01E05.zh.ass",
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
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/readme.txt", "readme.txt")));

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "Title", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileOperatorMock.Verify(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_StorePathNotExist_ReturnsEarly()
    {
        _fileStoreMock.Setup(s => s.ExistAsync("/missing", CancellationToken.None)).ReturnsAsync(false);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "Title", "/missing");

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileStoreMock.Verify(s => s.EnumerateDirectory(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_SanitizesInvalidChars()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/video.mp4", "video.mp4")));
        _fileOperatorMock.Setup(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        // Name with null char which is universally invalid
        var context = new FileRenameContext("Anime\0Title", 2, 3, "Title", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileOperatorMock.Verify(
            o => o.RenameAsync(
                "/downloads/anime/video.mp4",
                It.Is<string>(s => s.Contains("S02E03.mp4") && !s.Contains("\0")),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task RenameAsync_MultiEpisode_NoInference_LogsWarning()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.ExistAsync(storePath, CancellationToken.None)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(
                new FileStoreInfo(false, "/downloads/anime/ep1.mkv", "ep1.mkv"),
                new FileStoreInfo(false, "/downloads/anime/ep2.mkv", "ep2.mkv")));

        // No inference engine
        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, null, "Title", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        // Should not rename since there's no inference engine
        _fileOperatorMock.Verify(o => o.RenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
