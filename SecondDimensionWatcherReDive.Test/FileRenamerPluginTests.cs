using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.PluginEvents;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;
using SecondDimensionWatcherReDive.Plugin.FileRenamer;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class FileRenamerPluginTests
{
    private Mock<IAnimationInfoRepository> _mockAnimationInfoRepo = null!;
    private Mock<IFileRenamer> _mockFileRenamer = null!;
    private Mock<IServiceScopeFactory> _mockScopeFactory = null!;
    private FileRenamerPlugin _plugin = null!;

    private static readonly Animation TestAnimation = new(
        Guid.NewGuid(), "12345", "My Anime", "My Anime Original", "/poster.jpg");

    [TestInitialize]
    public void Setup()
    {
        _mockAnimationInfoRepo = new Mock<IAnimationInfoRepository>();
        _mockFileRenamer = new Mock<IFileRenamer>();

        var mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScopeServiceProvider = new Mock<IServiceProvider>();

        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockScopeServiceProvider.Object);
        mockScopeServiceProvider
            .Setup(p => p.GetService(typeof(IAnimationInfoRepository)))
            .Returns(_mockAnimationInfoRepo.Object);
        mockScopeServiceProvider
            .Setup(p => p.GetService(typeof(IFileRenamer)))
            .Returns(_mockFileRenamer.Object);

        _plugin = new FileRenamerPlugin(
            _mockScopeFactory.Object,
            Mock.Of<ILogger<FileRenamerPlugin>>());
    }

    [TestMethod]
    public async Task OnDownloadCompleted_ValidAnimationInfo_CallsRenameAsync()
    {
        var itemId = Guid.NewGuid();
        var info = CreateTestInfo(itemId, season: 1, episode: 5, storePath: "/downloads/anime",
            fileStore: "local", animation: TestAnimation);

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync(info);

        var pluginEvent = CreatePluginEventAndLoad();
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));

        _mockFileRenamer.Verify(
            r => r.RenameAsync(
                It.Is<FileRenameRequest>(c =>
                    c.AnimationName == "My Anime" &&
                    c.Season == 1 &&
                    c.Episode == 5 &&
                    c.StorePath == "/downloads/anime"),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task OnDownloadCompleted_AnimationNull_SkipsRename()
    {
        var itemId = Guid.NewGuid();
        var info = CreateTestInfo(itemId, storePath: "/downloads/anime", fileStore: "local", animation: null);

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync(info);

        var pluginEvent = CreatePluginEventAndLoad();
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));

        _mockFileRenamer.Verify(
            r => r.RenameAsync(It.IsAny<FileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockFileRenamer.Verify(
            r => r.RenameMultipleAsync(It.IsAny<MultipleFileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task OnDownloadCompleted_StorePathNull_SkipsRename()
    {
        var itemId = Guid.NewGuid();
        var info = CreateTestInfo(itemId, storePath: null, fileStore: "local", animation: TestAnimation);

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync(info);

        var pluginEvent = CreatePluginEventAndLoad();
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));

        _mockFileRenamer.Verify(
            r => r.RenameAsync(It.IsAny<FileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockFileRenamer.Verify(
            r => r.RenameMultipleAsync(It.IsAny<MultipleFileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task OnDownloadCompleted_AnimationInfoNotFound_SkipsRename()
    {
        var itemId = Guid.NewGuid();

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync((AnimationInfo?)null);

        var pluginEvent = CreatePluginEventAndLoad();
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));

        _mockFileRenamer.Verify(
            r => r.RenameAsync(It.IsAny<FileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockFileRenamer.Verify(
            r => r.RenameMultipleAsync(It.IsAny<MultipleFileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task OnDownloadCompleted_RenameThrows_DoesNotPropagate()
    {
        var itemId = Guid.NewGuid();
        var info = CreateTestInfo(itemId, season: 1, episode: 1, storePath: "/downloads/anime",
            fileStore: "local", animation: TestAnimation);

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync(info);
        _mockFileRenamer
            .Setup(r => r.RenameAsync(It.IsAny<FileRenameRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Rename failed"));

        var pluginEvent = CreatePluginEventAndLoad();

        // Should not throw — the plugin catches exceptions internally
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));
    }

    [TestMethod]
    public async Task OnDownloadCompleted_SeasonNull_DefaultsToOne()
    {
        var itemId = Guid.NewGuid();
        var info = CreateTestInfo(itemId, season: null, episode: 3, storePath: "/downloads/anime",
            fileStore: "local", animation: TestAnimation);

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync(info);

        var pluginEvent = CreatePluginEventAndLoad();
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));

        _mockFileRenamer.Verify(
            r => r.RenameAsync(
                It.Is<FileRenameRequest>(c => c.Season == 1),
                CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task OnDownloadCompleted_EpisodeNull_CallsRenameMultipleAsync()
    {
        var itemId = Guid.NewGuid();
        var info = CreateTestInfo(itemId, season: 1, episode: null, storePath: "/downloads/anime",
            fileStore: "local", animation: TestAnimation);

        _mockAnimationInfoRepo
            .Setup(r => r.FindByIdWithAnimationAsync(itemId, CancellationToken.None))
            .ReturnsAsync(info);

        var pluginEvent = CreatePluginEventAndLoad();
        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(itemId, "/downloads/anime", "local"));

        _mockFileRenamer.Verify(
            r => r.RenameMultipleAsync(
                It.Is<MultipleFileRenameRequest>(c =>
                    c.AnimationName == "My Anime" &&
                    c.Season == 1 &&
                    c.Path == "/downloads/anime"),
                CancellationToken.None),
            Times.Once);
        _mockFileRenamer.Verify(
            r => r.RenameAsync(It.IsAny<FileRenameRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public void PluginInfo_HasCorrectValues()
    {
        Assert.AreEqual("FileRenamer", _plugin.Info.Name);
        Assert.IsFalse(string.IsNullOrEmpty(_plugin.Info.Description));
    }

    /// <summary>
    ///     Creates a PluginEvent, wires it through PluginServices, and calls OnLoaded on the plugin.
    /// </summary>
    private PluginEvent<FileDownloadCompleteParam> CreatePluginEventAndLoad()
    {
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>();
        var beforeDownloadEvent = new PluginEvent<FileDownloadStartParam>();

        var mockRootServiceProvider = new Mock<IServiceProvider>();
        mockRootServiceProvider
            .Setup(p => p.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);

        var services = new PluginServices(mockRootServiceProvider.Object);
        services.AddEvent(PluginEventName.BeforeDownloadStarted, beforeDownloadEvent);
        services.AddEvent(PluginEventName.OnFileDownloadCompleted, pluginEvent);

        _plugin.OnLoaded(services);

        return pluginEvent;
    }

    private static AnimationInfo CreateTestInfo(
        Guid id,
        int? season = null,
        int? episode = null,
        string? storePath = null,
        string? fileStore = null,
        Animation? animation = null) =>
        new(id, "Test Title", "Test Description", DateTimeOffset.Now,
            "", "", Array.Empty<byte>(), "",
            false, default, default, true,
            fileStore, storePath, season, episode,
            null, animation, false, 0);
}
