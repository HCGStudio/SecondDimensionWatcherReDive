using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class MediaLibraryScannerTests
{
    [TestMethod]
    public async Task ScanAsync_LeaseUnavailable_ReturnsNullWithoutScanning()
    {
        using var fixture = new ScannerFixture();
        fixture.SourceRepository.Setup(repository => repository.TryAcquireScanLeaseAsync(
                fixture.SourceId,
                CancellationToken.None))
            .ReturnsAsync((IMediaLibraryScanLease?)null);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(0, fixture.LeaseDisposeCount);
        fixture.SourceRepository.Verify(repository => repository.FindByIdAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.GetByMediaLibrarySourceAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.SourceRepository.Verify(repository => repository.UpdateScanResultAsync(
            It.IsAny<Guid>(),
            It.IsAny<DateTimeOffset>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ScanAsync_FirstLevelDirectoryAndRootVideo_ImportsBothWithoutChangingFiles()
    {
        using var fixture = new ScannerFixture();
        var seriesDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Series A"));
        var firstEpisode = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series A - 01.mkv"),
            [0x01, 0x02, 0x03]);
        var secondEpisode = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series A - 02.mp4"),
            [0x04, 0x05]);
        var subtitle = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series A - 01.zh.ass"),
            [0x06]);
        var singleVideo = fixture.CreateFile(
            Path.Combine(fixture.RootPath, "Standalone Movie.mkv"),
            [0x07, 0x08, 0x09, 0x0a]);
        fixture.MakeStable(firstEpisode, secondEpisode, subtitle, singleVideo);
        var filesBefore = SnapshotFiles(fixture.RootPath);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        Assert.AreEqual(2, fixture.Added.Count);

        var directoryImport = fixture.Added.Single(info =>
            string.Equals(info.StorePath, seriesDirectory.FullName, StringComparison.Ordinal));
        AssertImportedInfo(
            directoryImport,
            fixture.SourceId,
            seriesDirectory.FullName,
            "Series A",
            6);

        var singleImport = fixture.Added.Single(info =>
            string.Equals(info.StorePath, singleVideo, StringComparison.Ordinal));
        AssertImportedInfo(
            singleImport,
            fixture.SourceId,
            singleVideo,
            "Standalone Movie",
            4);

        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.Is<Guid>(id => fixture.Added.Any(info => info.Id == id)),
            CancellationToken.None), Times.Exactly(2));
        fixture.SourceRepository.Verify(repository => repository.UpdateScanResultAsync(
            fixture.SourceId,
            It.IsAny<DateTimeOffset>(),
            null,
            2,
            0,
            0,
            0,
            CancellationToken.None), Times.Once);
        Assert.AreEqual(1, fixture.LeaseDisposeCount);
        var sourceCalls = fixture.SourceRepository.Invocations
            .Select(invocation => invocation.Method.Name)
            .ToList();
        Assert.IsTrue(
            sourceCalls.IndexOf(nameof(IMediaLibrarySourceRepository.TryAcquireScanLeaseAsync))
            < sourceCalls.IndexOf(nameof(IMediaLibrarySourceRepository.FindByIdAsync)),
            "The distributed lease must be acquired before the source is read.");

        var filesAfter = SnapshotFiles(fixture.RootPath);
        CollectionAssert.AreEquivalent(filesBefore.Keys.ToArray(), filesAfter.Keys.ToArray());
        foreach (var (path, expectedContent) in filesBefore)
        {
            Assert.IsTrue(File.Exists(path), $"Imported file was moved or removed: {path}");
            CollectionAssert.AreEqual(expectedContent, filesAfter[path], $"Imported file changed: {path}");
        }
        Assert.IsTrue(Directory.Exists(seriesDirectory.FullName));
    }

    [TestMethod]
    public async Task ScanAsync_TopLevelVideosWithSharedPrefix_AssignsSubtitleOnlyToLongestStem()
    {
        using var fixture = new ScannerFixture();
        var firstVideo = fixture.CreateFile(
            Path.Combine(fixture.RootPath, "Show.mkv"),
            [0x71, 0x72]);
        var secondVideo = fixture.CreateFile(
            Path.Combine(fixture.RootPath, "Show 2.mkv"),
            [0x73, 0x74, 0x75]);
        var secondSubtitle = fixture.CreateFile(
            Path.Combine(fixture.RootPath, "Show 2.srt"),
            [0x76, 0x77, 0x78, 0x79, 0x7a]);
        fixture.MakeStable(firstVideo, secondVideo, secondSubtitle);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);

        var firstImport = fixture.Added.Single(info => info.StorePath == firstVideo);
        var secondImport = fixture.Added.Single(info => info.StorePath == secondVideo);
        AssertImportedInfo(firstImport, fixture.SourceId, firstVideo, "Show", 2);
        AssertImportedInfo(secondImport, fixture.SourceId, secondVideo, "Show 2", 8);
        Assert.AreEqual(
            new FileInfo(secondVideo).Length + new FileInfo(secondSubtitle).Length,
            secondImport.ReleaseSizeBytes);
        Assert.AreEqual(
            new FileInfo(firstVideo).Length,
            firstImport.ReleaseSizeBytes,
            "Show 2.srt must not also be assigned to the shorter Show stem.");
    }

    [TestMethod]
    public async Task ScanAsync_RepeatedScanWithMatchingMappings_SkipsWithoutAddingOrRemapping()
    {
        using var fixture = new ScannerFixture();
        var seriesDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Series B"));
        var video = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series B - 01.mkv"),
            [0x11, 0x12]);
        var subtitle = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series B - 01.srt"),
            [0x13]);
        fixture.MakeStable(video, subtitle);

        var firstResult = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);
        Assert.IsNotNull(firstResult);
        Assert.AreEqual(1, firstResult.ImportedCount);
        var imported = fixture.Added.Single();
        fixture.SetMappings(imported.Id, video, subtitle);
        fixture.AnimationInfoRepository.Invocations.Clear();
        fixture.FileMapper.Invocations.Clear();

        var secondResult = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(secondResult);
        Assert.AreEqual(0, secondResult.ImportedCount);
        Assert.AreEqual(0, secondResult.UpdatedCount);
        Assert.AreEqual(0, secondResult.RemovedCount);
        Assert.AreEqual(1, secondResult.SkippedCount);
        Assert.IsNull(secondResult.Error);
        Assert.AreEqual(1, fixture.Added.Count);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ScanAsync_ExistingImportGainsVideo_UpdatesMetadataAndRemaps()
    {
        using var fixture = new ScannerFixture();
        var seriesDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Series C"));
        var firstVideo = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series C - 01.mkv"),
            [0x21, 0x22]);
        fixture.MakeStable(firstVideo);
        var existing = fixture.SeedExisting(
            seriesDirectory.FullName,
            FileDownloadTypes.MediaLibraryImport,
            stateVersion: 9);
        fixture.SetMappings(existing.Id, firstVideo);

        var secondVideo = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Series C - 02.mkv"),
            [0x23, 0x24, 0x25]);
        fixture.MakeStable(secondVideo);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(1, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(info =>
                info.Id == existing.Id
                && info.StorePath == seriesDirectory.FullName
                && info.ReleaseSizeBytes == 5),
            9,
            CancellationToken.None), Times.Once);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            existing.Id,
            CancellationToken.None), Times.Once);
        Assert.IsTrue(File.Exists(firstVideo));
        Assert.IsTrue(File.Exists(secondVideo));
    }

    [TestMethod]
    public async Task ScanAsync_NewVideoWithinSettlingPeriod_IsSkipped()
    {
        using var fixture = new ScannerFixture(settlingPeriod: TimeSpan.FromSeconds(30));
        var video = fixture.CreateFile(
            Path.Combine(fixture.RootPath, "Still Copying.mkv"),
            [0x31]);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(1, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(File.Exists(video));
        CollectionAssert.AreEqual(new byte[] { 0x31 }, File.ReadAllBytes(video));
    }

    [TestMethod]
    public async Task ScanAsync_NonVideoFilesAndNestedDirectoryWithoutVideo_AreNotImported()
    {
        using var fixture = new ScannerFixture();
        var notes = fixture.CreateFile(
            Path.Combine(fixture.RootPath, "notes.txt"),
            [0x41]);
        var extras = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Extras", "Nested"));
        var artwork = fixture.CreateFile(
            Path.Combine(extras.FullName, "poster.jpg"),
            [0x42]);
        var subtitle = fixture.CreateFile(
            Path.Combine(extras.FullName, "orphan.srt"),
            [0x43]);
        fixture.MakeStable(notes, artwork, subtitle);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(File.Exists(notes));
        Assert.IsTrue(File.Exists(artwork));
        Assert.IsTrue(File.Exists(subtitle));
    }

    [TestMethod]
    public async Task ScanAsync_SameStorageLocationOwnedByNonImportDownload_IsSkipped()
    {
        using var fixture = new ScannerFixture();
        var seriesDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Downloaded Series"));
        var video = fixture.CreateFile(
            Path.Combine(seriesDirectory.FullName, "Downloaded Series - 01.mkv"),
            [0x51, 0x52]);
        fixture.MakeStable(video);
        var foreign = fixture.SeedExisting(
            seriesDirectory.FullName,
            FileDownloadTypes.TorrentDownload,
            stateVersion: 4);
        fixture.SetMappings(foreign.Id, video);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(1, result.SkippedCount);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error, "already owned by another media entry");
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMappingRepository.Verify(repository => repository.GetForAnimationInfosAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 1 && ids.Contains(foreign.Id)),
            CancellationToken.None), Times.Once);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(File.Exists(video));
    }

    [TestMethod]
    public async Task ScanAsync_OneTickFilesystemPrecisionDifference_SkipsUnchangedThenReusesIdAfterRename()
    {
        using var fixture = new ScannerFixture();
        var oldDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Tick Release"));
        var oldVideo = fixture.CreateFile(
            Path.Combine(oldDirectory.FullName, "Episode 01.mkv"),
            [0x91, 0x92, 0x93]);
        var filesystemWriteTime = SetOneTickPastMicrosecond(oldVideo);

        var firstResult = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(firstResult);
        Assert.AreEqual(1, firstResult.ImportedCount);
        var imported = fixture.Added.Single();
        Assert.AreEqual(
            1L,
            filesystemWriteTime.UtcDateTime.Ticks - imported.DownloadEndTime.UtcDateTime.Ticks,
            "The fixture must reproduce PostgreSQL's microsecond precision boundary.");
        fixture.SetMappings(imported.Id, oldVideo);
        fixture.AnimationInfoRepository.Invocations.Clear();
        fixture.FileMapper.Invocations.Clear();

        var repeatResult = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(repeatResult);
        Assert.AreEqual(0, repeatResult.ImportedCount);
        Assert.AreEqual(0, repeatResult.UpdatedCount);
        Assert.AreEqual(0, repeatResult.RemovedCount);
        Assert.AreEqual(1, repeatResult.SkippedCount);
        Assert.IsNull(repeatResult.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);

        fixture.AnimationInfoRepository.Invocations.Clear();
        fixture.FileMapper.Invocations.Clear();
        var renamedDirectory = Path.Combine(fixture.RootPath, "Tick Release Renamed");
        Directory.Move(oldDirectory.FullName, renamedDirectory);
        var renamedVideo = Path.Combine(renamedDirectory, "Episode 01.mkv");

        var renameResult = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(renameResult);
        Assert.AreEqual(0, renameResult.ImportedCount);
        Assert.AreEqual(1, renameResult.UpdatedCount);
        Assert.AreEqual(0, renameResult.RemovedCount);
        Assert.AreEqual(0, renameResult.SkippedCount);
        Assert.IsNull(renameResult.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(info =>
                info.Id == imported.Id
                && info.StorePath == renamedDirectory
                && info.DownloadEndTime == imported.DownloadEndTime),
            imported.StateVersion,
            CancellationToken.None), Times.Once);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            imported.Id,
            CancellationToken.None), Times.Once);
        fixture.AnimationInfoRepository.Verify(repository => repository.RemoveMediaLibraryEntryAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMappingRepository.Verify(repository => repository.RemoveByAnimationInfoAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(imported.Id, fixture.GetExisting(imported.Id)!.Id);
        Assert.AreEqual(renamedDirectory, fixture.GetExisting(imported.Id)!.StorePath);
        Assert.IsTrue(File.Exists(renamedVideo));
        CollectionAssert.AreEqual(new byte[] { 0x91, 0x92, 0x93 }, File.ReadAllBytes(renamedVideo));
    }

    [TestMethod]
    public async Task ScanAsync_ImportedCandidateRenamed_ReusesEntryAndKeepsRenamedFile()
    {
        using var fixture = new ScannerFixture();
        var oldDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Old Release"));
        var oldVideo = fixture.CreateFile(
            Path.Combine(oldDirectory.FullName, "Episode 01.mkv"),
            [0x61, 0x62, 0x63]);
        fixture.MakeStable(oldVideo);
        var originalWriteTime = TruncateToMicrosecond(new DateTimeOffset(
            File.GetLastWriteTimeUtc(oldVideo),
            TimeSpan.Zero));
        var stale = fixture.SeedExisting(
            oldDirectory.FullName,
            FileDownloadTypes.MediaLibraryImport,
            stateVersion: 6,
            releaseSizeBytes: 3,
            downloadEndTime: originalWriteTime,
            missingSince: originalWriteTime.AddHours(-1));
        fixture.SetMappings(stale.Id, oldVideo);

        var newDirectoryPath = Path.Combine(fixture.RootPath, "Renamed Release");
        Directory.Move(oldDirectory.FullName, newDirectoryPath);
        var renamedVideo = Path.Combine(newDirectoryPath, "Episode 01.mkv");
        var expectedContent = File.ReadAllBytes(renamedVideo);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(1, result.UpdatedCount);
        Assert.AreEqual(0, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.AddAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(info =>
                info.Id == stale.Id
                && info.StorePath == newDirectoryPath
                && info.ReleaseSizeBytes == 3
                && info.MediaLibraryMissingSince == null),
            6,
            CancellationToken.None), Times.Once);
        fixture.AnimationInfoRepository.Verify(repository => repository.RemoveMediaLibraryEntryAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual(0, fixture.Removed.Count);
        fixture.FileMapper.Verify(mapper => mapper.MapDownloadAsync(
            stale.Id,
            CancellationToken.None), Times.Once);
        Assert.AreEqual(stale.Id, fixture.GetExisting(stale.Id)!.Id);
        Assert.AreEqual(newDirectoryPath, fixture.GetExisting(stale.Id)!.StorePath);

        Assert.IsFalse(Directory.Exists(oldDirectory.FullName));
        Assert.IsFalse(File.Exists(oldVideo));
        Assert.IsTrue(Directory.Exists(newDirectoryPath));
        Assert.IsTrue(File.Exists(renamedVideo), "Scanner must never delete or move the renamed source file.");
        CollectionAssert.AreEqual(
            expectedContent,
            File.ReadAllBytes(renamedVideo),
            "Scanner must not modify the renamed source file.");
    }

    [TestMethod]
    public async Task ScanAsync_FirstMissingObservation_SetsMissingSinceAndRemovesOnlyMappings()
    {
        using var fixture = new ScannerFixture(missingGracePeriod: TimeSpan.FromHours(1));
        var directory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Temporarily Missing"));
        var video = fixture.CreateFile(
            Path.Combine(directory.FullName, "Episode 01.mkv"),
            [0x81, 0x82]);
        fixture.MakeStable(video);
        var existing = fixture.SeedExisting(
            directory.FullName,
            FileDownloadTypes.MediaLibraryImport,
            stateVersion: 12,
            releaseSizeBytes: 2,
            downloadEndTime: new DateTimeOffset(File.GetLastWriteTimeUtc(video), TimeSpan.Zero));
        fixture.SetMappings(existing.Id, video);
        Directory.Delete(directory.FullName, recursive: true);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(1, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(info =>
                info.Id == existing.Id
                && info.StorePath == existing.StorePath
                && info.MediaLibraryMissingSince != null),
            12,
            CancellationToken.None), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.RemoveByAnimationInfoAsync(
            existing.Id,
            CancellationToken.None), Times.Once);
        fixture.AnimationInfoRepository.Verify(repository => repository.RemoveMediaLibraryEntryAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsNotNull(fixture.GetExisting(existing.Id));
        Assert.IsNotNull(fixture.GetExisting(existing.Id)!.MediaLibraryMissingSince);
        CollectionAssert.Contains(fixture.SoftRemovedMappings, existing.Id);
        Assert.AreEqual(0, fixture.Removed.Count);
    }

    [TestMethod]
    public async Task ScanAsync_UnownedMissingEntryUnderReaddedSource_IsClaimedAndSoftRetired()
    {
        using var fixture = new ScannerFixture(missingGracePeriod: TimeSpan.FromHours(1));
        var missingDirectory = Path.Combine(fixture.RootPath, "Removed Source Entry");
        var missingVideo = Path.Combine(missingDirectory, "Episode 01.mkv");
        var orphan = fixture.SeedExisting(
            missingDirectory,
            FileDownloadTypes.MediaLibraryImport,
            stateVersion: 18,
            releaseSizeBytes: 7,
            downloadEndTime: DateTimeOffset.UtcNow.AddHours(-2),
            isUnowned: true);
        fixture.SetMappings(orphan.Id, missingVideo);

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(1, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository =>
            repository.GetUnownedMediaLibraryEntriesUnderPathAsync(
                FileStores.LocalDiskStore,
                fixture.RootPath,
                CancellationToken.None), Times.Once);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.Is<AnimationInfo>(info =>
                info.Id == orphan.Id
                && info.MediaLibrarySourceId == fixture.SourceId
                && info.MediaLibraryMissingSince != null),
            18,
            CancellationToken.None), Times.Once);
        fixture.FileMappingRepository.Verify(repository => repository.RemoveByAnimationInfoAsync(
            orphan.Id,
            CancellationToken.None), Times.Once);
        fixture.AnimationInfoRepository.Verify(repository => repository.RemoveMediaLibraryEntryAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        var claimed = fixture.GetExisting(orphan.Id);
        Assert.IsNotNull(claimed);
        Assert.AreEqual(fixture.SourceId, claimed.MediaLibrarySourceId);
        Assert.IsNotNull(claimed.MediaLibraryMissingSince);
        CollectionAssert.Contains(fixture.SoftRemovedMappings, orphan.Id);
        Assert.AreEqual(0, fixture.Removed.Count);
        Assert.IsFalse(Directory.Exists(missingDirectory));
        Assert.IsFalse(File.Exists(missingVideo));
    }

    [TestMethod]
    public async Task ScanAsync_AlreadyMissingBeyondGracePeriod_HardDeletesOnNextFullScan()
    {
        using var fixture = new ScannerFixture(missingGracePeriod: TimeSpan.FromMinutes(30));
        var missingPath = Path.Combine(fixture.RootPath, "Long Missing");
        var existing = fixture.SeedExisting(
            missingPath,
            FileDownloadTypes.MediaLibraryImport,
            stateVersion: 15,
            releaseSizeBytes: 4,
            downloadEndTime: DateTimeOffset.UtcNow.AddHours(-2),
            missingSince: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await fixture.Scanner.ScanAsync(
            fixture.SourceId,
            CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.ImportedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(1, result.RemovedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.IsNull(result.Error);
        fixture.AnimationInfoRepository.Verify(repository => repository.TryUpdateAsync(
            It.IsAny<AnimationInfo>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.FileMappingRepository.Verify(repository => repository.RemoveByAnimationInfoAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.AnimationInfoRepository.Verify(repository => repository.RemoveMediaLibraryEntryAsync(
            existing.Id,
            fixture.SourceId,
            CancellationToken.None), Times.Once);
        Assert.IsNull(fixture.GetExisting(existing.Id));
        CollectionAssert.Contains(fixture.Removed, existing.Id);
    }

    private static void AssertImportedInfo(
        AnimationInfo info,
        Guid expectedSourceId,
        string expectedStorePath,
        string expectedTitle,
        long expectedSize)
    {
        Assert.AreEqual(expectedTitle, info.Title);
        Assert.IsTrue(info.IsDownloadFinished);
        Assert.IsTrue(info.IsDownloadTracked);
        Assert.AreEqual(FileStores.LocalDiskStore, info.FileStore);
        Assert.AreEqual(expectedStorePath, info.StorePath);
        Assert.AreEqual(FileDownloadTypes.MediaLibraryImport, info.DownloadType);
        Assert.IsFalse(info.IsAiProcessed);
        Assert.AreEqual(0, info.AiRetryCount);
        Assert.AreEqual(MetadataReviewStatus.Pending, info.MetadataStatus);
        Assert.AreEqual(expectedSize, info.ReleaseSizeBytes);
        Assert.AreEqual(expectedSourceId, info.MediaLibrarySourceId);
    }

    private static Dictionary<string, byte[]> SnapshotFiles(string rootPath) =>
        Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFullPath(path),
                File.ReadAllBytes,
                PathComparer);

    private static DateTimeOffset SetOneTickPastMicrosecond(string path)
    {
        var stableTicks = DateTimeOffset.UtcNow
            .Subtract(TimeSpan.FromMinutes(2))
            .UtcDateTime.Ticks;
        stableTicks -= stableTicks % TimeSpan.TicksPerMicrosecond;
        var requested = new DateTimeOffset(stableTicks + 1, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(path, requested.UtcDateTime);
        var actual = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        if (actual.UtcDateTime.Ticks % TimeSpan.TicksPerMicrosecond != 1)
            Assert.Inconclusive(
                "The test filesystem does not preserve 100-nanosecond mtime precision.");
        return actual;
    }

    private static DateTimeOffset TruncateToMicrosecond(DateTimeOffset value)
    {
        var ticks = value.UtcDateTime.Ticks;
        return new DateTimeOffset(
            ticks - ticks % TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed class ScannerFixture : IDisposable
    {
        private readonly Dictionary<string, AnimationInfo> _existingByPath =
            new(PathComparer);
        private readonly Dictionary<Guid, IReadOnlyList<FileMapping>> _mappings = new();

        public ScannerFixture(
            TimeSpan? settlingPeriod = null,
            TimeSpan? missingGracePeriod = null)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"sdw-media-library-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
            SourceId = Guid.NewGuid();
            var source = new MediaLibrarySource(
                SourceId,
                RootPath,
                IsMonitoring: true,
                CreatedAt: DateTimeOffset.UtcNow,
                LastScanAt: null,
                LastError: null,
                LastImportedCount: 0,
                LastUpdatedCount: 0,
                LastRemovedCount: 0,
                LastSkippedCount: 0);

            SourceRepository.Setup(repository => repository.FindByIdAsync(
                    SourceId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(source);
            SourceRepository.Setup(repository => repository.TryAcquireScanLeaseAsync(
                    SourceId,
                    It.IsAny<CancellationToken>()))
                .Returns(() => Task.FromResult<IMediaLibraryScanLease?>(
                    new TestScanLease(() => LeaseDisposeCount++)));
            SourceRepository.Setup(repository => repository.UpdateScanResultAsync(
                    SourceId,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<string?>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            AnimationInfoRepository.Setup(repository => repository.GetByStorageLocationsAsync(
                    FileStores.LocalDiskStore,
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, IReadOnlyCollection<string> paths, CancellationToken _) =>
                    Task.FromResult<IReadOnlyList<AnimationInfo>>(
                        _existingByPath.Values
                            .Where(info => info.StorePath is not null
                                           && paths.Contains(info.StorePath, PathComparer))
                            .ToList()));
            AnimationInfoRepository.Setup(repository => repository.GetByMediaLibrarySourceAsync(
                    SourceId,
                    It.IsAny<CancellationToken>()))
                .Returns((Guid _, CancellationToken _) => Task.FromResult<IReadOnlyList<AnimationInfo>>(
                    _existingByPath.Values
                        .Where(info => info.MediaLibrarySourceId == SourceId
                                       && info.DownloadType == FileDownloadTypes.MediaLibraryImport)
                        .ToList()));
            AnimationInfoRepository.Setup(repository =>
                    repository.GetUnownedMediaLibraryEntriesUnderPathAsync(
                        FileStores.LocalDiskStore,
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                .Returns((string _, string sourcePath, CancellationToken _) =>
                    Task.FromResult<IReadOnlyList<AnimationInfo>>(
                        _existingByPath.Values
                            .Where(info => info.MediaLibrarySourceId is null
                                           && info.DownloadType == FileDownloadTypes.MediaLibraryImport
                                           && info.StorePath is not null
                                           && MediaLibraryPath.IsLexicallyAllowed(
                                               info.StorePath,
                                               [sourcePath]))
                            .ToList()));
            AnimationInfoRepository.Setup(repository => repository.GetByPhysicalPathsAsync(
                    FileStores.LocalDiskStore,
                    It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string _, IReadOnlyCollection<string> paths, CancellationToken _) =>
                {
                    var ids = _mappings
                        .Where(pair => pair.Value.Any(mapping => paths.Contains(mapping.PhysicalPath)))
                        .Select(pair => pair.Key)
                        .ToHashSet();
                    return Task.FromResult<IReadOnlyList<AnimationInfo>>(
                        _existingByPath.Values.Where(info => ids.Contains(info.Id)).ToList());
                });
            AnimationInfoRepository.Setup(repository => repository.AddAsync(
                    It.IsAny<AnimationInfo>(),
                    It.IsAny<CancellationToken>()))
                .Callback<AnimationInfo, CancellationToken>((info, _) =>
                {
                    Added.Add(info);
                    _existingByPath.Add(info.StorePath!, info);
                })
                .Returns(Task.CompletedTask);
            AnimationInfoRepository.Setup(repository => repository.TryUpdateAsync(
                    It.IsAny<AnimationInfo>(),
                    It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
                .Returns((AnimationInfo info, long _, CancellationToken _) =>
                {
                    var previous = _existingByPath.FirstOrDefault(candidate =>
                        candidate.Value.Id == info.Id);
                    if (previous.Value is not null
                        && !PathComparer.Equals(previous.Key, info.StorePath))
                        _existingByPath.Remove(previous.Key);
                    _existingByPath[info.StorePath!] = info;
                    return Task.FromResult(true);
                });
            AnimationInfoRepository.Setup(repository => repository.RemoveMediaLibraryEntryAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Guid id, Guid? expectedSourceId, CancellationToken _) =>
                {
                    var pair = _existingByPath.FirstOrDefault(candidate =>
                        candidate.Value.Id == id
                        && candidate.Value.MediaLibrarySourceId == expectedSourceId
                        && candidate.Value.DownloadType == FileDownloadTypes.MediaLibraryImport);
                    if (pair.Value is null) return Task.FromResult(false);

                    _existingByPath.Remove(pair.Key);
                    _mappings.Remove(id);
                    Removed.Add(id);
                    return Task.FromResult(true);
                });

            FileMappingRepository.Setup(repository => repository.GetForAnimationInfosAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                {
                    var requestedIds = ids.ToHashSet();
                    return Task.FromResult<IReadOnlyList<FileMapping>>(
                        _mappings
                            .Where(pair => requestedIds.Contains(pair.Key))
                            .SelectMany(pair => pair.Value)
                            .ToList());
                });
            FileMappingRepository.Setup(repository => repository.RemoveByAnimationInfoAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Guid, CancellationToken>((id, _) =>
                {
                    _mappings.Remove(id);
                    SoftRemovedMappings.Add(id);
                })
                .Returns(Task.CompletedTask);
            FileMapper.Setup(mapper => mapper.MapDownloadAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            Options.SetupGet(monitor => monitor.CurrentValue)
                .Returns(new MediaLibraryOptions
                {
                    SettlingPeriod = settlingPeriod ?? TimeSpan.FromSeconds(30),
                    MissingGracePeriod = missingGracePeriod ?? TimeSpan.FromHours(24),
                    AllowedRoots = [RootPath]
                });

            Scanner = new MediaLibraryScanner(
                SourceRepository.Object,
                AnimationInfoRepository.Object,
                FileMappingRepository.Object,
                FileMapper.Object,
                Options.Object,
                NullLogger<MediaLibraryScanner>.Instance);
        }

        public string RootPath { get; }
        public Guid SourceId { get; }
        public List<AnimationInfo> Added { get; } = [];
        public List<Guid> Removed { get; } = [];
        public List<Guid> SoftRemovedMappings { get; } = [];
        public int LeaseDisposeCount { get; private set; }
        public Mock<IMediaLibrarySourceRepository> SourceRepository { get; } = new();
        public Mock<IAnimationInfoRepository> AnimationInfoRepository { get; } = new();
        public Mock<IFileMappingRepository> FileMappingRepository { get; } = new();
        public Mock<IFileMapper> FileMapper { get; } = new();
        public Mock<IOptionsMonitor<MediaLibraryOptions>> Options { get; } = new();
        public MediaLibraryScanner Scanner { get; }

        public string CreateFile(string path, byte[] content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            return Path.GetFullPath(path);
        }

        public void MakeStable(params string[] paths)
        {
            var stableTime = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2));
            foreach (var path in paths) File.SetLastWriteTimeUtc(path, stableTime);
        }

        public AnimationInfo SeedExisting(
            string storePath,
            string downloadType,
            long stateVersion,
            long? releaseSizeBytes = null,
            DateTimeOffset? downloadEndTime = null,
            DateTimeOffset? missingSince = null,
            bool isUnowned = false)
        {
            var path = Path.GetFullPath(storePath);
            var timestamp = downloadEndTime
                            ?? DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(2));
            var info = new AnimationInfo(
                Id: Guid.NewGuid(),
                Title: "Existing item",
                Description: string.Empty,
                PublishTime: timestamp,
                DownloadUrl: string.Empty,
                DownloadType: downloadType,
                CachedDownloadData: [],
                AdditionalDownloadInfo: string.Empty,
                IsDownloadTracked: true,
                DownloadStartTime: timestamp,
                DownloadEndTime: timestamp,
                IsDownloadFinished: true,
                FileStore: FileStores.LocalDiskStore,
                StorePath: path,
                Season: null,
                Episode: null,
                Group: null,
                Animation: null,
                IsAiProcessed: false,
                AiRetryCount: 0,
                ReleaseSizeBytes: releaseSizeBytes,
                MetadataStatus: MetadataReviewStatus.Pending,
                StateVersion: stateVersion,
                MediaLibrarySourceId: downloadType == FileDownloadTypes.MediaLibraryImport
                                      && !isUnowned
                    ? SourceId
                    : null,
                MediaLibraryMissingSince: missingSince);
            _existingByPath.Add(path, info);
            return info;
        }

        public AnimationInfo? GetExisting(Guid id) =>
            _existingByPath.Values.FirstOrDefault(info => info.Id == id);

        public void SetMappings(Guid animationInfoId, params string[] physicalPaths)
        {
            _mappings[animationInfoId] = physicalPaths
                .Select((path, index) => new FileMapping(
                    Guid.NewGuid(),
                    animationInfoId,
                    $"/test/{animationInfoId:N}/{index}",
                    Path.GetFullPath(path),
                    FileStores.LocalDiskStore))
                .ToList();
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }

        private sealed class TestScanLease(Action onDispose) : IMediaLibraryScanLease
        {
            private int _isDisposed;

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _isDisposed, 1) == 0) onDispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
