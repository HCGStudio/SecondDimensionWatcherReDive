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
        _fileStoreMock.Setup(s => s.Exist(storePath)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/[Group] Title - 05.mkv", "[Group] Title - 05.mkv")));
        _fileOperatorMock.Setup(o => o.Rename(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "[Group] Title - 05", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileOperatorMock.Verify(
            o => o.Rename(
                "/downloads/anime/[Group] Title - 05.mkv",
                "/downloads/anime/My Anime S01E05.mkv"),
            Times.Once);
    }

    [TestMethod]
    public async Task RenameAsync_NoVideoFiles_DoesNotRename()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.Exist(storePath)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/readme.txt", "readme.txt")));

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "Title", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileOperatorMock.Verify(o => o.Rename(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_StorePathNotExist_ReturnsEarly()
    {
        _fileStoreMock.Setup(s => s.Exist("/missing")).ReturnsAsync(false);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, 5, "Title", "/missing");

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileStoreMock.Verify(s => s.EnumerateDirectory(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RenameAsync_SanitizesInvalidChars()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.Exist(storePath)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(new FileStoreInfo(false, "/downloads/anime/video.mp4", "video.mp4")));
        _fileOperatorMock.Setup(o => o.Rename(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        // Name with null char which is universally invalid
        var context = new FileRenameContext("Anime\0Title", 2, 3, "Title", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        _fileOperatorMock.Verify(
            o => o.Rename(
                "/downloads/anime/video.mp4",
                It.Is<string>(s => s.Contains("S02E03.mp4") && !s.Contains("\0"))),
            Times.Once);
    }

    [TestMethod]
    public async Task RenameAsync_MultiEpisode_NoInference_LogsWarning()
    {
        var storePath = "/downloads/anime";
        _fileStoreMock.Setup(s => s.Exist(storePath)).ReturnsAsync(true);
        _fileStoreMock.Setup(s => s.EnumerateDirectory(storePath))
            .Returns(ToAsyncEnumerable(
                new FileStoreInfo(false, "/downloads/anime/ep1.mkv", "ep1.mkv"),
                new FileStoreInfo(false, "/downloads/anime/ep2.mkv", "ep2.mkv")));

        // No inference engine
        var renamer = new VideoFileRenamer(_fileStoreMock.Object, _fileOperatorMock.Object, _loggerMock.Object);
        var context = new FileRenameContext("My Anime", 1, null, "Title", storePath);

        await renamer.RenameAsync(context, CancellationToken.None);

        // Should not rename since there's no inference engine
        _fileOperatorMock.Verify(o => o.Rename(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
