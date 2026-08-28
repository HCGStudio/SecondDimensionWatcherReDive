using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class MediaLibraryControllerTests
{
    [TestMethod]
    public async Task CreateSource_RelativePath_ReturnsBadRequestWithoutPersistingOrQueueing()
    {
        var repository = new Mock<IMediaLibrarySourceRepository>();
        var queue = new Mock<IMediaLibraryScanQueue>();
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            AllowedRoot(Path.GetTempPath()));

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest("relative/media", IsMonitoring: false),
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        repository.Verify(candidate => candidate.GetAllAsync(
            It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(candidate => candidate.TryAddAsync(
            It.IsAny<MediaLibrarySource>(),
            It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(candidate => candidate.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateSource_AbsoluteReadableDirectory_NormalizesPersistsAndQueues()
    {
        using var directory = new TemporaryDirectory();
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.GetAllAsync(CancellationToken.None))
            .ReturnsAsync(Array.Empty<MediaLibrarySource>());
        MediaLibrarySource? added = null;
        repository.Setup(candidate => candidate.TryAddAsync(
                It.IsAny<MediaLibrarySource>(),
                CancellationToken.None))
            .Callback<MediaLibrarySource, CancellationToken>((source, _) => added = source)
            .ReturnsAsync(true);
        var queue = new Mock<IMediaLibraryScanQueue>();
        queue.Setup(candidate => candidate.Enqueue(It.IsAny<Guid>())).Returns(true);
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            AllowedRoot(directory.Path));
        var requestedPath = directory.Path + System.IO.Path.DirectorySeparatorChar;

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest(requestedPath, IsMonitoring: true),
            CancellationToken.None);

        var created = result as CreatedAtActionResult;
        Assert.IsNotNull(created);
        Assert.IsNotNull(added);
        Assert.AreEqual(directory.Path, added.Path);
        Assert.IsTrue(added.IsMonitoring);
        var response = created.Value as MediaLibrarySourceResponse;
        Assert.IsNotNull(response);
        Assert.AreEqual(added.Id, response.Id);
        Assert.AreEqual(directory.Path, response.Path);
        Assert.AreEqual(0, response.LastRemovedCount);
        Assert.IsFalse(response.IsScanning);
        queue.Verify(candidate => candidate.Enqueue(added.Id), Times.Once);
    }

    [TestMethod]
    public async Task CreateSource_PathCoveredByExistingSource_ReturnsConflict()
    {
        using var directory = new TemporaryDirectory();
        var childPath = Directory.CreateDirectory(
            System.IO.Path.Combine(directory.Path, "child")).FullName;
        var existing = Source(directory.Path);
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.GetAllAsync(CancellationToken.None))
            .ReturnsAsync([existing]);
        var queue = new Mock<IMediaLibraryScanQueue>();
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            AllowedRoot(directory.Path));

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest(childPath, IsMonitoring: false),
            CancellationToken.None);

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
        repository.Verify(candidate => candidate.TryAddAsync(
            It.IsAny<MediaLibrarySource>(),
            It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(candidate => candidate.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateSource_PathOutsideAllowedRoots_ReturnsBadRequest()
    {
        using var allowed = new TemporaryDirectory();
        using var requested = new TemporaryDirectory();
        var repository = new Mock<IMediaLibrarySourceRepository>();
        var queue = new Mock<IMediaLibraryScanQueue>();
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            AllowedRoot(allowed.Path));

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest(requested.Path, IsMonitoring: false),
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        repository.Verify(candidate => candidate.TryAddAsync(
            It.IsAny<MediaLibrarySource>(),
            It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(candidate => candidate.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateSource_ReloadedAllowedRoots_UsesCurrentOptions()
    {
        using var originalRoot = new TemporaryDirectory();
        using var reloadedRoot = new TemporaryDirectory();
        var currentOptions = new MediaLibraryOptions
        {
            AllowedRoots = [originalRoot.Path]
        };
        var options = new Mock<IOptionsMonitor<MediaLibraryOptions>>();
        options.SetupGet(candidate => candidate.CurrentValue)
            .Returns(() => currentOptions);
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.GetAllAsync(CancellationToken.None))
            .ReturnsAsync(Array.Empty<MediaLibrarySource>());
        repository.Setup(candidate => candidate.TryAddAsync(
                It.IsAny<MediaLibrarySource>(),
                CancellationToken.None))
            .ReturnsAsync(true);
        var queue = new Mock<IMediaLibraryScanQueue>();
        queue.Setup(candidate => candidate.Enqueue(It.IsAny<Guid>())).Returns(true);
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            options.Object);
        currentOptions = new MediaLibraryOptions
        {
            AllowedRoots = [reloadedRoot.Path]
        };

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest(
                reloadedRoot.Path,
                IsMonitoring: false),
            CancellationToken.None);

        Assert.IsInstanceOfType<CreatedAtActionResult>(result);
        repository.Verify(candidate => candidate.TryAddAsync(
            It.Is<MediaLibrarySource>(source => source.Path == reloadedRoot.Path),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task CreateSource_NonUniqueDatabaseFailure_IsNotReportedAsConflict()
    {
        using var directory = new TemporaryDirectory();
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.GetAllAsync(CancellationToken.None))
            .ReturnsAsync(Array.Empty<MediaLibrarySource>());
        repository.Setup(candidate => candidate.TryAddAsync(
                It.IsAny<MediaLibrarySource>(),
                CancellationToken.None))
            .ThrowsAsync(new DbUpdateException("database unavailable"));
        var controller = new MediaLibraryController(
            repository.Object,
            Mock.Of<IMediaLibraryScanQueue>(),
            AllowedRoot(directory.Path));

        await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
            controller.CreateSource(
                new CreateMediaLibrarySourceRequest(
                    directory.Path,
                    IsMonitoring: false),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task CreateSource_PathOverlappingDownloadRoot_ReturnsBadRequestWithoutQueueing()
    {
        using var directory = new TemporaryDirectory();
        var downloadRoot = Directory.CreateDirectory(
            System.IO.Path.Combine(directory.Path, "managed-downloads")).FullName;
        var repository = new Mock<IMediaLibrarySourceRepository>();
        var queue = new Mock<IMediaLibraryScanQueue>();
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            AllowedRoot(directory.Path, downloadRoot));

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest(downloadRoot, IsMonitoring: true),
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        repository.Verify(candidate => candidate.GetAllAsync(
            It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(candidate => candidate.TryAddAsync(
            It.IsAny<MediaLibrarySource>(),
            It.IsAny<CancellationToken>()), Times.Never);
        queue.Verify(candidate => candidate.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task CreateSource_SymlinkAliasInsideAllowedRoot_PersistsResolvedDirectory()
    {
        using var directory = new TemporaryDirectory();
        var realPath = Directory.CreateDirectory(
            System.IO.Path.Combine(directory.Path, "real-media")).FullName;
        var aliasPath = System.IO.Path.Combine(directory.Path, "media-alias");
        if (!TryCreateDirectorySymlink(aliasPath, realPath, out var reason))
        {
            Assert.Inconclusive($"Symbolic links are unavailable in this environment: {reason}");
            return;
        }

        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.GetAllAsync(CancellationToken.None))
            .ReturnsAsync(Array.Empty<MediaLibrarySource>());
        MediaLibrarySource? added = null;
        repository.Setup(candidate => candidate.TryAddAsync(
                It.IsAny<MediaLibrarySource>(),
                CancellationToken.None))
            .Callback<MediaLibrarySource, CancellationToken>((source, _) => added = source)
            .ReturnsAsync(true);
        var queue = new Mock<IMediaLibraryScanQueue>();
        queue.Setup(candidate => candidate.Enqueue(It.IsAny<Guid>())).Returns(true);
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            AllowedRoot(directory.Path));

        var result = await controller.CreateSource(
            new CreateMediaLibrarySourceRequest(aliasPath, IsMonitoring: true),
            CancellationToken.None);

        var created = result as CreatedAtActionResult;
        Assert.IsNotNull(created);
        Assert.IsNotNull(added);
        Assert.AreEqual(realPath, added.Path);
        Assert.AreNotEqual(aliasPath, added.Path);
        var response = created.Value as MediaLibrarySourceResponse;
        Assert.IsNotNull(response);
        Assert.AreEqual(realPath, response.Path);
        queue.Verify(candidate => candidate.Enqueue(added.Id), Times.Once);
    }

    [TestMethod]
    public async Task ScanSource_RepeatedRequest_QueuesSourceOnlyOnce()
    {
        using var directory = new TemporaryDirectory();
        var source = Source(directory.Path);
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.FindByIdAsync(
                source.Id,
                CancellationToken.None))
            .ReturnsAsync(source);
        var queue = new MediaLibraryScanQueue();
        var controller = new MediaLibraryController(
            repository.Object,
            queue,
            Monitor(new MediaLibraryOptions()));

        var first = await controller.ScanSource(source.Id, CancellationToken.None)
            as AcceptedResult;
        var second = await controller.ScanSource(source.Id, CancellationToken.None)
            as AcceptedResult;

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        var firstResponse = first.Value as QueueMediaLibraryScanResponse;
        var secondResponse = second.Value as QueueMediaLibraryScanResponse;
        Assert.IsNotNull(firstResponse);
        Assert.IsNotNull(secondResponse);
        Assert.IsTrue(firstResponse.Queued);
        Assert.IsFalse(secondResponse.Queued);
        Assert.IsTrue(queue.IsQueuedOrRunning(source.Id));
    }

    [TestMethod]
    public async Task ScanSource_MissingSource_ReturnsNotFoundWithoutQueueing()
    {
        var sourceId = Guid.NewGuid();
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.FindByIdAsync(
                sourceId,
                CancellationToken.None))
            .ReturnsAsync((MediaLibrarySource?)null);
        var queue = new Mock<IMediaLibraryScanQueue>();
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            Monitor(new MediaLibraryOptions()));

        var result = await controller.ScanSource(sourceId, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
        queue.Verify(candidate => candidate.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteSource_QueuedLocally_ReturnsConflictWithoutRepositoryDelete()
    {
        var sourceId = Guid.NewGuid();
        var repository = new Mock<IMediaLibrarySourceRepository>();
        var queue = new Mock<IMediaLibraryScanQueue>();
        queue.Setup(candidate => candidate.IsQueuedOrRunning(sourceId)).Returns(true);
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            Monitor(new MediaLibraryOptions()));

        var result = await controller.DeleteSource(sourceId, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
        repository.Verify(candidate => candidate.TryRemoveByIdAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task DeleteSource_Removed_ReturnsNoContent()
    {
        var (controller, repository, sourceId) = DeleteFixture(
            MediaLibrarySourceRemoveResult.Removed);

        var result = await controller.DeleteSource(sourceId, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        repository.Verify(candidate => candidate.TryRemoveByIdAsync(
            sourceId,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSource_NotFound_ReturnsNotFound()
    {
        var (controller, repository, sourceId) = DeleteFixture(
            MediaLibrarySourceRemoveResult.NotFound);

        var result = await controller.DeleteSource(sourceId, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
        repository.Verify(candidate => candidate.TryRemoveByIdAsync(
            sourceId,
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSource_BusyAcrossInstances_ReturnsConflict()
    {
        var (controller, repository, sourceId) = DeleteFixture(
            MediaLibrarySourceRemoveResult.Busy);

        var result = await controller.DeleteSource(sourceId, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
        repository.Verify(candidate => candidate.TryRemoveByIdAsync(
            sourceId,
            CancellationToken.None), Times.Once);
    }

    private static MediaLibrarySource Source(string path) => new(
        Guid.NewGuid(),
        path,
        IsMonitoring: true,
        CreatedAt: DateTimeOffset.UtcNow,
        LastScanAt: null,
        LastError: null,
        LastImportedCount: 0,
        LastUpdatedCount: 0,
        LastRemovedCount: 0,
        LastSkippedCount: 0);

    private static IOptionsMonitor<MediaLibraryOptions> AllowedRoot(
        string path,
        string? downloadRoot = null) =>
        Monitor(new MediaLibraryOptions
        {
            AllowedRoots = [path],
            DownloadRoot = downloadRoot
        });

    private static IOptionsMonitor<MediaLibraryOptions> Monitor(
        MediaLibraryOptions value)
    {
        var monitor = new Mock<IOptionsMonitor<MediaLibraryOptions>>();
        monitor.SetupGet(candidate => candidate.CurrentValue).Returns(value);
        return monitor.Object;
    }

    private static (
        MediaLibraryController Controller,
        Mock<IMediaLibrarySourceRepository> Repository,
        Guid SourceId) DeleteFixture(MediaLibrarySourceRemoveResult removeResult)
    {
        var sourceId = Guid.NewGuid();
        var repository = new Mock<IMediaLibrarySourceRepository>();
        repository.Setup(candidate => candidate.TryRemoveByIdAsync(
                sourceId,
                CancellationToken.None))
            .ReturnsAsync(removeResult);
        var queue = new Mock<IMediaLibraryScanQueue>();
        queue.Setup(candidate => candidate.IsQueuedOrRunning(sourceId)).Returns(false);
        var controller = new MediaLibraryController(
            repository.Object,
            queue.Object,
            Monitor(new MediaLibraryOptions()));
        return (controller, repository, sourceId);
    }

    private static bool TryCreateDirectorySymlink(
        string linkPath,
        string targetPath,
        out string reason)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException
                                          or NotSupportedException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"sdw-media-library-controller-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
