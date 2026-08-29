using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class FileExplorerTests
{
    [TestMethod]
    public async Task GetDirectoryEntriesAsync_UsesOneDirectChildQueryAndOneStatBatch()
    {
        var repository = new Mock<IFileMappingRepository>(MockBehavior.Strict);
        var store = new Mock<IFileStore>(MockBehavior.Strict);
        var provider = new Mock<IFileStoreProvider>(MockBehavior.Strict);
        var first = Mapping("/anime/one.mkv", "/disk/one.mkv");
        var second = Mapping("/anime/two.mkv", "/disk/two.mkv");
        repository.Setup(candidate => candidate.GetImmediateChildrenAsync(
                "/anime",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FileSystemEntry>)
            [
                new FileSystemEntry("/anime/extras", "/anime", "extras", true, null),
                new FileSystemEntry(first.VirtualPath, "/anime", "one.mkv", false, first),
                new FileSystemEntry(second.VirtualPath, "/anime", "two.mkv", false, second)
            ]);
        provider.Setup(candidate => candidate.GetRequiredClient("local"))
            .Returns(store.Object);
        store.Setup(candidate => candidate.GetFileInfosAsync(
                It.Is<IReadOnlyCollection<string>>(paths => paths.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, FileStoreInfo>(StringComparer.Ordinal)
            {
                [first.PhysicalPath] = new(false, first.PhysicalPath, "one.mkv", 1, DateTimeOffset.UnixEpoch),
                [second.PhysicalPath] = new(false, second.PhysicalPath, "two.mkv", 2, DateTimeOffset.UnixEpoch)
            });
        var explorer = new FileExplorer(repository.Object, provider.Object);

        var entries = await explorer.GetDirectoryEntriesAsync(
            new DirectoryToken("/anime", "anime"),
            CancellationToken.None);

        Assert.HasCount(3, entries);
        Assert.AreEqual(1, entries.Single(entry => entry.FileName == "one.mkv").FileInfo?.Length);
        Assert.AreEqual(2, entries.Single(entry => entry.FileName == "two.mkv").FileInfo?.Length);
        repository.VerifyAll();
        store.VerifyAll();
        provider.VerifyAll();
    }

    private static FileMapping Mapping(string virtualPath, string physicalPath) =>
        new(Guid.NewGuid(), Guid.NewGuid(), virtualPath, physicalPath, "local");
}
