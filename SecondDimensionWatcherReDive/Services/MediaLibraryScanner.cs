using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Services;

public interface IMediaLibraryScanner
{
    Task<MediaLibraryScanResult?> ScanAsync(Guid sourceId, CancellationToken cancellationToken);
}

public sealed record MediaLibraryScanResult(
    Guid SourceId,
    int ImportedCount,
    int UpdatedCount,
    int RemovedCount,
    int SkippedCount,
    string? Error);

public partial class MediaLibraryScanner(
    IMediaLibrarySourceRepository sourceRepository,
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
    IFileMapper fileMapper,
    IOptionsMonitor<MediaLibraryOptions> options,
    ILogger<MediaLibraryScanner> logger) : IMediaLibraryScanner
{
    private const int MaxStoredErrorLength = 2048;

    public async Task<MediaLibraryScanResult?> ScanAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        // The in-memory queue deduplicates one process. A session advisory lease
        // serializes the complete discover/reconcile/map unit across app instances.
        await using var scanLease = await sourceRepository.TryAcquireScanLeaseAsync(
            sourceId,
            cancellationToken);
        if (scanLease is null)
        {
            LogSourceAlreadyScanning(logger, sourceId);
            return null;
        }

        // Re-read only after acquiring the lease so a concurrent cross-instance
        // delete cannot invalidate the FK halfway through the scan.
        var source = await sourceRepository.FindByIdAsync(sourceId, cancellationToken);
        if (source is null) return null;

        var importedCount = 0;
        var updatedCount = 0;
        var removedCount = 0;
        var skippedCount = 0;
        var errors = new List<string>();
        var scanStartedAt = DateTimeOffset.UtcNow;

        try
        {
            if (!Directory.Exists(source.Path))
                throw new DirectoryNotFoundException($"Media library path does not exist: {source.Path}");

            var currentOptions = options.CurrentValue;
            var sourcePath = MediaLibraryPath.ResolveExistingPath(source.Path);
            if (!PathComparer.Equals(
                    sourcePath,
                    MediaLibraryPath.Normalize(source.Path))
                || !MediaLibraryPath.IsAllowed(sourcePath, currentOptions.AllowedRoots))
                throw new UnauthorizedAccessException(
                    "Media library path is outside the configured allowed roots or now traverses a symbolic link.");

            if (!string.IsNullOrWhiteSpace(currentOptions.DownloadRoot)
                && MediaLibraryPath.PathsOverlap(
                    sourcePath,
                    currentOptions.DownloadRoot))
                throw new InvalidOperationException(
                    "Media library source overlaps the managed download directory.");

            // Discovery must finish successfully before reconciliation. An inaccessible
            // or transiently missing directory aborts the scan so a partial snapshot can
            // never make valid database entries look deleted.
            var candidates = DiscoverCandidates(sourcePath, cancellationToken);
            EnsureSourceStillAllowed(source.Path, sourcePath, currentOptions);

            var ownedEntries = await animationInfoRepository.GetByMediaLibrarySourceAsync(
                source.Id,
                cancellationToken);
            // Deleting a source intentionally leaves its imported rows behind with a
            // null FK. Include every such row covered by the newly configured root so
            // re-adding a source can adopt present entries and retire absent ones.
            var unownedEntries = await animationInfoRepository
                .GetUnownedMediaLibraryEntriesUnderPathAsync(
                    FileStores.LocalDiskStore,
                    sourcePath,
                    cancellationToken);
            var trackedEntries = ownedEntries
                .Concat(unownedEntries)
                .DistinctBy(entry => entry.Id)
                .ToList();

            var candidateStorePaths = candidates
                .Select(candidate => candidate.FullPath)
                .Distinct(PathComparer)
                .ToArray();
            var storageMatches = await animationInfoRepository.GetByStorageLocationsAsync(
                FileStores.LocalDiskStore,
                candidateStorePaths,
                cancellationToken);
            var storageMatchesByPath = storageMatches
                .Where(entry => entry.StorePath is not null)
                .GroupBy(entry => entry.StorePath!, PathComparer)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    PathComparer);
            var exactEntries = candidates.ToDictionary(
                candidate => candidate.FullPath,
                candidate => SelectExactEntry(
                    storageMatchesByPath.GetValueOrDefault(candidate.FullPath),
                    source.Id),
                PathComparer);

            var physicalPaths = candidates
                .SelectMany(candidate => candidate.Files)
                .Select(file => file.FullPath)
                .Distinct(PathComparer)
                .ToArray();
            var physicalMatches = await animationInfoRepository.GetByPhysicalPathsAsync(
                FileStores.LocalDiskStore,
                physicalPaths,
                cancellationToken);
            var entriesNeedingMappings = physicalMatches
                .Concat(exactEntries.Values.OfType<AnimationInfo>())
                .Concat(trackedEntries)
                .DistinctBy(info => info.Id)
                .ToList();
            var mappingRows = await fileMappingRepository.GetForAnimationInfosAsync(
                entriesNeedingMappings.Select(entry => entry.Id).ToArray(),
                cancellationToken);
            var mappingsByEntry = mappingRows
                .GroupBy(mapping => mapping.AnimationInfoId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<FileMapping>)group.ToList());

            var assignments = AssignExistingEntries(
                source.Id,
                candidates,
                exactEntries,
                physicalMatches,
                trackedEntries,
                mappingsByEntry);
            var assignedIds = assignments.Values
                .Select(entry => entry.Id)
                .ToHashSet();

            // Entries owned by this source that disappeared, plus unowned remnants
            // covered by a newly reconfigured source, are first soft-retired. Their
            // virtual mappings disappear, but the media row and its review/playback
            // history survive a transient empty NAS snapshot or rename settling window.
            var entriesToRetire = trackedEntries
                .Where(entry => !assignedIds.Contains(entry.Id))
                .Concat(physicalMatches.Where(entry =>
                    entry.DownloadType == FileDownloadTypes.MediaLibraryImport
                    && entry.MediaLibrarySourceId is null
                    && !assignedIds.Contains(entry.Id)))
                .DistinctBy(entry => entry.Id)
                .ToList();
            var missingGracePeriod = currentOptions.MissingGracePeriod;
            if (missingGracePeriod < TimeSpan.Zero) missingGracePeriod = TimeSpan.Zero;
            var entriesToDelete = new List<AnimationInfo>();

            EnsureSourceStillAllowed(source.Path, sourcePath, currentOptions);
            foreach (var entry in entriesToRetire)
            {
                try
                {
                    if (entry.MediaLibrarySourceId == source.Id
                        && entry.MediaLibraryMissingSince is { } missingSince
                        && scanStartedAt - missingSince >= missingGracePeriod)
                    {
                        entriesToDelete.Add(entry);
                        continue;
                    }

                    var newlyMissing = entry.MediaLibraryMissingSince is null;
                    var retired = entry with
                    {
                        MediaLibrarySourceId = source.Id,
                        MediaLibraryMissingSince = entry.MediaLibraryMissingSince
                                                   ?? scanStartedAt
                    };
                    if ((entry.MediaLibrarySourceId != retired.MediaLibrarySourceId
                         || entry.MediaLibraryMissingSince != retired.MediaLibraryMissingSince)
                        && !await animationInfoRepository.TryUpdateAsync(
                            retired,
                            entry.StateVersion,
                            cancellationToken))
                    {
                        errors.Add($"{entry.StorePath}: media entry changed concurrently during reconciliation");
                        continue;
                    }

                    var mappings = mappingsByEntry.GetValueOrDefault(entry.Id)
                                   ?? Array.Empty<FileMapping>();
                    if (mappings.Count > 0)
                        await fileMappingRepository.RemoveByAnimationInfoAsync(
                            entry.Id,
                            cancellationToken);
                    if (newlyMissing) removedCount++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogCandidateFailed(logger, ex, entry.StorePath ?? entry.Id.ToString());
                    errors.Add($"{entry.StorePath}: {SafeError(ex)}");
                }
            }

            var settlingPeriod = currentOptions.SettlingPeriod;
            if (settlingPeriod < TimeSpan.Zero) settlingPeriod = TimeSpan.Zero;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (DateTimeOffset.UtcNow - candidate.LatestWriteTime < settlingPeriod)
                    {
                        skippedCount++;
                        continue;
                    }

                    assignments.TryGetValue(candidate.FullPath, out var existing);
                    if (existing is null)
                    {
                        if (HasForeignOwner(
                                source.Id,
                                candidate,
                                exactEntries,
                                physicalMatches,
                                mappingsByEntry))
                        {
                            skippedCount++;
                            errors.Add($"{candidate.RelativePath}: physical files are already owned by another media entry");
                            continue;
                        }

                        var info = CreateAnimationInfo(source, candidate);
                        await animationInfoRepository.AddAsync(info, cancellationToken);
                        importedCount++;

                        if (!await fileMapper.MapDownloadAsync(info.Id, cancellationToken))
                            errors.Add($"{candidate.RelativePath}: virtual mapping could not be created");
                        continue;
                    }

                    var mappings = mappingsByEntry.GetValueOrDefault(existing.Id)
                                   ?? Array.Empty<FileMapping>();
                    var mappingChanged = !SamePhysicalFiles(candidate.Files, mappings)
                                         || !PathComparer.Equals(
                                             existing.StorePath,
                                             candidate.FullPath);
                    var refreshed = existing with
                    {
                        PublishTime = candidate.LatestWriteTime,
                        DownloadUrl = BuildDownloadUrl(source.Id, candidate.RelativePath),
                        AdditionalDownloadInfo = candidate.RelativePath,
                        DownloadStartTime = candidate.LatestWriteTime,
                        DownloadEndTime = candidate.LatestWriteTime,
                        FileStore = FileStores.LocalDiskStore,
                        StorePath = candidate.FullPath,
                        ReleaseSizeBytes = candidate.TotalSize,
                        MediaLibrarySourceId = source.Id,
                        MediaLibraryMissingSince = null
                    };
                    var metadataChanged = HasImportMetadataChanged(existing, refreshed);
                    if (!metadataChanged && !mappingChanged)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (metadataChanged
                        && !await animationInfoRepository.TryUpdateAsync(
                            refreshed,
                            existing.StateVersion,
                            cancellationToken))
                    {
                        errors.Add($"{candidate.RelativePath}: media metadata changed concurrently");
                        continue;
                    }

                    if (mappingChanged
                        && !await fileMapper.MapDownloadAsync(existing.Id, cancellationToken))
                    {
                        errors.Add($"{candidate.RelativePath}: virtual mapping could not be refreshed");
                        continue;
                    }

                    updatedCount++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogCandidateFailed(logger, ex, candidate.FullPath);
                    errors.Add($"{candidate.RelativePath}: {SafeError(ex)}");
                }
            }

            // Hard-delete only after at least two missing observations and the
            // configured grace period. This remains database-only and is deliberately
            // after candidate processing so a successful rename/adoption keeps its ID.
            EnsureSourceStillAllowed(source.Path, sourcePath, currentOptions);
            foreach (var entry in entriesToDelete)
            {
                if (await animationInfoRepository.RemoveMediaLibraryEntryAsync(
                        entry.Id,
                        source.Id,
                        cancellationToken))
                    removedCount++;
                else
                    errors.Add($"{entry.StorePath}: media entry changed concurrently during cleanup");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSourceFailed(logger, ex, source.Id, source.Path);
            errors.Add(SafeError(ex));
        }

        var error = errors.Count == 0
            ? null
            : Truncate(string.Join("; ", errors.Distinct(StringComparer.Ordinal)));
        var scannedAt = DateTimeOffset.UtcNow;
        await sourceRepository.UpdateScanResultAsync(
            source.Id,
            scannedAt,
            error,
            importedCount,
            updatedCount,
            removedCount,
            skippedCount,
            cancellationToken);

        LogSourceCompleted(
            logger,
            source.Id,
            importedCount,
            updatedCount,
            removedCount,
            skippedCount,
            errors.Count);
        return new MediaLibraryScanResult(
            source.Id,
            importedCount,
            updatedCount,
            removedCount,
            skippedCount,
            error);
    }

    private static AnimationInfo CreateAnimationInfo(
        MediaLibrarySource source,
        MediaLibraryCandidate candidate)
    {
        var now = candidate.LatestWriteTime;
        return new AnimationInfo(
            Guid.NewGuid(),
            candidate.Title,
            $"Imported from local media library: {candidate.RelativePath}",
            now,
            BuildDownloadUrl(source.Id, candidate.RelativePath),
            FileDownloadTypes.MediaLibraryImport,
            Array.Empty<byte>(),
            candidate.RelativePath,
            IsDownloadTracked: true,
            DownloadStartTime: now,
            DownloadEndTime: now,
            IsDownloadFinished: true,
            FileStore: FileStores.LocalDiskStore,
            StorePath: candidate.FullPath,
            Season: null,
            Episode: null,
            Group: null,
            Animation: null,
            IsAiProcessed: false,
            AiRetryCount: 0,
            ReleaseSizeBytes: candidate.TotalSize,
            MetadataStatus: MetadataReviewStatus.Pending,
            MediaLibrarySourceId: source.Id,
            ReleaseIdentity: ReleaseIdentity.CreateMediaImport(
                source.Id,
                FileStores.LocalDiskStore,
                candidate.FullPath));
    }

    private static string BuildDownloadUrl(Guid sourceId, string relativePath) =>
        $"media-library://{sourceId}/{Uri.EscapeDataString(relativePath)}";

    private static Dictionary<string, AnimationInfo> AssignExistingEntries(
        Guid sourceId,
        IReadOnlyList<MediaLibraryCandidate> candidates,
        IReadOnlyDictionary<string, AnimationInfo?> exactEntries,
        IReadOnlyList<AnimationInfo> physicalMatches,
        IReadOnlyList<AnimationInfo> trackedEntries,
        IReadOnlyDictionary<Guid, IReadOnlyList<FileMapping>> mappingsByEntry)
    {
        var assignments = new Dictionary<string, AnimationInfo>(PathComparer);
        var usedEntryIds = new HashSet<Guid>();

        // An exact candidate identity wins first. This is the common scan path and
        // also claims records whose former source was removed (SourceId was SET NULL).
        foreach (var candidate in candidates)
        {
            var exact = exactEntries.GetValueOrDefault(candidate.FullPath);
            if (!CanBeClaimed(exact, sourceId) || !usedEntryIds.Add(exact!.Id)) continue;
            assignments[candidate.FullPath] = exact;
        }

        // A source root can be removed and re-added at a different level, changing
        // candidate StorePath boundaries. Match remaining candidates by the physical
        // files already mapped so those records are adopted instead of duplicated.
        foreach (var candidate in candidates.Where(candidate =>
                     !assignments.ContainsKey(candidate.FullPath)))
        {
            var candidatePaths = candidate.Files
                .Select(file => file.FullPath)
                .ToHashSet(PathComparer);
            var match = physicalMatches
                .Where(entry => CanBeClaimed(entry, sourceId)
                                && !usedEntryIds.Contains(entry.Id))
                .Select(entry => new
                {
                    Entry = entry,
                    Overlap = mappingsByEntry.GetValueOrDefault(entry.Id)?
                        .Count(mapping => candidatePaths.Contains(mapping.PhysicalPath)) ?? 0
                })
                .Where(candidateMatch => candidateMatch.Overlap > 0)
                .OrderByDescending(candidateMatch =>
                    candidateMatch.Entry.MediaLibrarySourceId == sourceId)
                .ThenByDescending(candidateMatch => candidateMatch.Overlap)
                .ThenBy(candidateMatch => candidateMatch.Entry.Id)
                .Select(candidateMatch => candidateMatch.Entry)
                .FirstOrDefault();
            if (match is null) continue;

            usedEntryIds.Add(match.Id);
            assignments[candidate.FullPath] = match;
        }

        // A pure rename changes every physical path, so mapping overlap cannot
        // identify it. Preserve the aggregate ID only for an unambiguous size +
        // last-write fingerprint match in both directions.
        var remainingCandidates = candidates
            .Where(candidate => !assignments.ContainsKey(candidate.FullPath))
            .ToList();
        var availableTrackedEntries = trackedEntries
            .Where(entry => CanBeClaimed(entry, sourceId)
                            && !usedEntryIds.Contains(entry.Id))
            .ToList();
        var fingerprintMatches = remainingCandidates.ToDictionary(
            candidate => candidate.FullPath,
            candidate => availableTrackedEntries
                .Where(entry => entry.ReleaseSizeBytes == candidate.TotalSize
                                && entry.DownloadEndTime == candidate.LatestWriteTime)
                .ToList(),
            PathComparer);
        foreach (var candidate in remainingCandidates)
        {
            var matches = fingerprintMatches[candidate.FullPath];
            if (matches.Count != 1) continue;

            var match = matches[0];
            var matchingCandidateCount = fingerprintMatches.Values.Count(entries =>
                entries.Any(entry => entry.Id == match.Id));
            if (matchingCandidateCount != 1 || !usedEntryIds.Add(match.Id)) continue;
            assignments[candidate.FullPath] = match;
        }

        return assignments;
    }

    private static AnimationInfo? SelectExactEntry(
        IReadOnlyList<AnimationInfo>? entries,
        Guid sourceId)
    {
        if (entries is null || entries.Count == 0) return null;

        // A claimable media import is the stable identity for this physical root.
        // Other download types can legally share the same StorePath because the
        // database uniqueness constraint is filtered to media-library imports.
        return entries.FirstOrDefault(entry => CanBeClaimed(entry, sourceId))
               ?? entries.OrderBy(entry => entry.Id).First();
    }

    private static bool CanBeClaimed(AnimationInfo? entry, Guid sourceId) =>
        entry is not null
        && string.Equals(
            entry.DownloadType,
            FileDownloadTypes.MediaLibraryImport,
            StringComparison.Ordinal)
        && (entry.MediaLibrarySourceId is null || entry.MediaLibrarySourceId == sourceId);

    private static bool HasForeignOwner(
        Guid sourceId,
        MediaLibraryCandidate candidate,
        IReadOnlyDictionary<string, AnimationInfo?> exactEntries,
        IReadOnlyList<AnimationInfo> physicalMatches,
        IReadOnlyDictionary<Guid, IReadOnlyList<FileMapping>> mappingsByEntry)
    {
        var exact = exactEntries.GetValueOrDefault(candidate.FullPath);
        if (exact is not null
            && (!string.Equals(
                    exact.DownloadType,
                    FileDownloadTypes.MediaLibraryImport,
                    StringComparison.Ordinal)
                || (exact.MediaLibrarySourceId is not null
                    && exact.MediaLibrarySourceId != sourceId)))
            return true;

        var candidatePaths = candidate.Files
            .Select(file => file.FullPath)
            .ToHashSet(PathComparer);
        return physicalMatches.Any(entry =>
            (!string.Equals(
                 entry.DownloadType,
                 FileDownloadTypes.MediaLibraryImport,
                 StringComparison.Ordinal)
             || (entry.MediaLibrarySourceId is not null
                 && entry.MediaLibrarySourceId != sourceId))
            && (mappingsByEntry.GetValueOrDefault(entry.Id)?.Any(mapping =>
                candidatePaths.Contains(mapping.PhysicalPath)) ?? false));
    }

    private static bool HasImportMetadataChanged(AnimationInfo current, AnimationInfo refreshed) =>
        current.PublishTime != refreshed.PublishTime
        || !string.Equals(current.DownloadUrl, refreshed.DownloadUrl, StringComparison.Ordinal)
        || !string.Equals(
            current.AdditionalDownloadInfo,
            refreshed.AdditionalDownloadInfo,
            StringComparison.Ordinal)
        || current.DownloadStartTime != refreshed.DownloadStartTime
        || current.DownloadEndTime != refreshed.DownloadEndTime
        || !string.Equals(current.FileStore, refreshed.FileStore, StringComparison.Ordinal)
        || !PathComparer.Equals(current.StorePath, refreshed.StorePath)
        || current.ReleaseSizeBytes != refreshed.ReleaseSizeBytes
        || current.MediaLibrarySourceId != refreshed.MediaLibrarySourceId
        || current.MediaLibraryMissingSince != refreshed.MediaLibraryMissingSince;

    private static void EnsureSourceStillAllowed(
        string configuredPath,
        string expectedPhysicalPath,
        MediaLibraryOptions options)
    {
        var currentPhysicalPath = MediaLibraryPath.ResolveExistingPath(configuredPath);
        if (!PathComparer.Equals(
                currentPhysicalPath,
                MediaLibraryPath.Normalize(configuredPath))
            || !PathComparer.Equals(currentPhysicalPath, expectedPhysicalPath)
            || !MediaLibraryPath.IsAllowed(currentPhysicalPath, options.AllowedRoots))
            throw new UnauthorizedAccessException(
                "Media library source changed or left the configured allowed roots during the scan.");

        if (!string.IsNullOrWhiteSpace(options.DownloadRoot)
            && MediaLibraryPath.PathsOverlap(currentPhysicalPath, options.DownloadRoot))
            throw new InvalidOperationException(
                "Media library source overlaps the managed download directory.");
    }

    private static bool SamePhysicalFiles(
        IReadOnlyList<MediaLibraryFile> files,
        IReadOnlyList<FileMapping> mappings)
    {
        var expected = files
            .Select(file => file.FullPath)
            .ToHashSet(PathComparer);
        var actual = mappings
            .Select(mapping => mapping.PhysicalPath)
            .ToHashSet(PathComparer);
        return expected.Count == files.Count
               && actual.Count == mappings.Count
               && expected.SetEquals(actual);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static IReadOnlyList<MediaLibraryCandidate> DiscoverCandidates(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var source = new DirectoryInfo(sourcePath);
        var candidates = new List<MediaLibraryCandidate>();
        var topLevelOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var directory in source.EnumerateDirectories("*", topLevelOptions)
                     .OrderBy(directory => directory.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            var files = EnumerateFiles(directory.FullName, recursive: true, cancellationToken);
            if (!files.Any(file => MediaFileTypes.IsVideo(file.FullPath))) continue;
            candidates.Add(CreateCandidate(
                directory.FullName,
                directory.Name,
                directory.Name,
                files));
        }

        var topLevelFiles = source.EnumerateFiles("*", topLevelOptions)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var topLevelVideos = topLevelFiles
            .Where(file => MediaFileTypes.IsVideo(file.Name))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var sidecarsByVideo = topLevelFiles
            .Where(file => MediaFileTypes.IsSubtitle(file.Name))
            .Select(file => new
            {
                File = file,
                VideoName = MediaFileTypes.FindBestVideoForSubtitle(
                    topLevelVideos.Select(video => video.Name),
                    file.Name)
            })
            .Where(match => match.VideoName is not null)
            .GroupBy(match => match.VideoName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(match => match.File).ToList(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var file in topLevelVideos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            var snapshots = new List<MediaLibraryFile> { CreateFile(file) };
            if (sidecarsByVideo.TryGetValue(file.Name, out var sidecars))
                snapshots.AddRange(sidecars.Select(CreateFile));
            candidates.Add(CreateCandidate(
                file.FullName,
                file.Name,
                Path.GetFileNameWithoutExtension(file.Name),
                snapshots));
        }

        return candidates
            .OrderBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static List<MediaLibraryFile> EnumerateFiles(
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var files = new List<MediaLibraryFile>();
        foreach (var filePath in Directory.EnumerateFiles(path, "*", enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(CreateFile(new FileInfo(filePath)));
        }

        return files
            .OrderBy(file => file.FullPath, StringComparer.Ordinal)
            .ToList();
    }

    private static MediaLibraryFile CreateFile(FileInfo file) => new(
        file.FullName,
        file.Length,
        NormalizeDatabaseTimestamp(
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));

    // PostgreSQL timestamptz stores microseconds while DateTimeOffset and common
    // filesystems can expose 100-nanosecond ticks. Quantize at discovery so a
    // database round-trip cannot make an unchanged file look updated or prevent
    // a pure rename from matching its existing aggregate.
    private static DateTimeOffset NormalizeDatabaseTimestamp(DateTimeOffset value)
    {
        var utcTicks = value.UtcDateTime.Ticks;
        return new DateTimeOffset(
            utcTicks - utcTicks % TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);
    }

    private static MediaLibraryCandidate CreateCandidate(
        string fullPath,
        string relativePath,
        string title,
        IReadOnlyList<MediaLibraryFile> files)
    {
        var latest = files.Max(file => file.LastWriteTime);
        var totalSize = files.Aggregate(0L, (total, file) => total + file.Length);
        return new MediaLibraryCandidate(
            fullPath,
            relativePath,
            title,
            files,
            latest,
            totalSize);
    }

    private static string SafeError(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

    private static string Truncate(string value) =>
        value.Length <= MaxStoredErrorLength ? value : value[..MaxStoredErrorLength];

    private sealed record MediaLibraryFile(
        string FullPath,
        long Length,
        DateTimeOffset LastWriteTime);

    private sealed record MediaLibraryCandidate(
        string FullPath,
        string RelativePath,
        string Title,
        IReadOnlyList<MediaLibraryFile> Files,
        DateTimeOffset LatestWriteTime,
        long TotalSize);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Media library candidate {Path} could not be imported")]
    private static partial void LogCandidateFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Media library source {SourceId} at {Path} could not be scanned")]
    private static partial void LogSourceFailed(
        ILogger logger,
        Exception exception,
        Guid sourceId,
        string path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Media library source {SourceId} is already being scanned by another app instance")]
    private static partial void LogSourceAlreadyScanning(ILogger logger, Guid sourceId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Media library source {SourceId} scan completed: {ImportedCount} imported, {UpdatedCount} updated, {RemovedCount} removed, {SkippedCount} skipped, {ErrorCount} errors")]
    private static partial void LogSourceCompleted(
        ILogger logger,
        Guid sourceId,
        int importedCount,
        int updatedCount,
        int removedCount,
        int skippedCount,
        int errorCount);
}
