using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public interface IFileMapper
{
    Task<bool> MapDownloadAsync(Guid animationInfoId, CancellationToken cancellationToken);

    Task<bool> ReidentifyFilesWithAiAsync(Guid animationInfoId, CancellationToken cancellationToken);

    Task<FileMappingPreview?> PreviewDownloadAsync(
        AnimationInfo proposedInfo,
        CancellationToken cancellationToken);
}

public sealed record FileMappingPreview(
    IReadOnlyList<FileMapping> Mappings,
    IReadOnlyList<string> Warnings);

internal sealed class AiFileNameInferenceUnavailableException()
    : InvalidOperationException("AI filename inference is not configured.")
{
}

public partial class FileMapper(
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
    IFileNameRegexRuleRepository fileNameRegexRuleRepository,
    IFileStoreProvider fileStoreProvider,
    ILogger<FileMapper> logger,
    IInferenceEngine? inferenceEngine = null) : IFileMapper
{
    private const string UnknownRoot = "/unknown";

    public Task<bool> MapDownloadAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        return MapDownloadCoreAsync(animationInfoId, false, cancellationToken);
    }

    public Task<bool> ReidentifyFilesWithAiAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        if (inferenceEngine is null)
            throw new AiFileNameInferenceUnavailableException();

        return MapDownloadCoreAsync(animationInfoId, true, cancellationToken);
    }

    public async Task<FileMappingPreview?> PreviewDownloadAsync(
        AnimationInfo proposedInfo,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var mappings = await PlanDownloadAsync(
            proposedInfo,
            forceAi: false,
            allowAi: false,
            warnings,
            cancellationToken);
        if (mappings is null) return null;

        if (mappings.Any(mapping => mapping.VirtualPath.StartsWith(
                UnknownRoot + "/",
                StringComparison.Ordinal)))
            warnings.Add("unresolvedFiles");

        return new FileMappingPreview(
            mappings,
            warnings.Distinct(StringComparer.Ordinal).ToList());
    }

    private async Task<bool> MapDownloadCoreAsync(
        Guid animationInfoId,
        bool forceAi,
        CancellationToken cancellationToken)
    {
        var info = await animationInfoRepository.FindByIdWithAnimationAsync(animationInfoId, cancellationToken);
        if (info is null)
        {
            LogAnimationInfoNotFound(logger, animationInfoId);
            return false;
        }

        var mappings = await PlanDownloadAsync(
            info,
            forceAi,
            allowAi: true,
            warnings: null,
            cancellationToken);
        if (mappings is null) return false;
        if (mappings.Count == 0) return false;

        // A manual AI-only retry is all-or-nothing. Partial results must not replace a
        // previously complete mapping and move the unresolved episodes to /unknown.
        if (forceAi && !AllVideoFilesIdentified(mappings))
        {
            LogForcedInferenceNoResult(logger, animationInfoId);
            return false;
        }

        // Idempotent: replace any prior mappings for this AnimationInfo so re-runs
        // (e.g. after post-download inference fills in the canonical path) don't
        // collide with the unique VirtualPath index.
        var replaced = await fileMappingRepository.ReplaceForAnimationInfoAsync(
            info.Id,
            info.StateVersion,
            info.FileStore!,
            info.StorePath!,
            mappings,
            cancellationToken);
        if (!replaced)
        {
            LogDownloadChangedDuringMapping(logger, animationInfoId);
            return false;
        }
        LogMapped(logger, animationInfoId, mappings.Count);
        return true;
    }

    private async Task<List<FileMapping>?> PlanDownloadAsync(
        AnimationInfo info,
        bool forceAi,
        bool allowAi,
        List<string>? warnings,
        CancellationToken cancellationToken)
    {
        if (info.FileStore is null || info.StorePath is null)
        {
            LogNoStorePath(logger, info.Id);
            return null;
        }

        var store = fileStoreProvider.GetClient(info.FileStore);
        if (store is null)
        {
            LogNoFileStore(logger, info.FileStore);
            return null;
        }

        var files = await EnumerateFilesAsync(store, info.StorePath, cancellationToken);
        if (string.Equals(
                info.DownloadType,
                FileDownloadTypes.MediaLibraryImport,
                StringComparison.Ordinal))
            files = await IncludeImportedFileSidecarsAsync(
                store,
                info.StorePath,
                files,
                cancellationToken);
        if (files.Count == 0)
        {
            LogNoFiles(logger, info.StorePath);
            return null;
        }

        return await BuildMappingsAsync(
            info,
            files,
            forceAi,
            allowAi,
            warnings,
            cancellationToken);
    }

    private static bool AllVideoFilesIdentified(
        IReadOnlyList<FileMapping> mappings)
    {
        var videos = mappings
            .Where(mapping => MediaFileTypes.VideoExtensions.Contains(Path.GetExtension(mapping.PhysicalPath)))
            .ToList();
        if (videos.Count == 0) return false;

        foreach (var video in videos)
        {
            if (video.VirtualPath.StartsWith(UnknownRoot + "/", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private sealed record DiscoveredFile(string PhysicalPath, string FileName, string RelativePath);

    private static async Task<List<DiscoveredFile>> IncludeImportedFileSidecarsAsync(
        IFileStore store,
        string rootPath,
        List<DiscoveredFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count != 1
            || !MediaLibraryPath.PathEquals(files[0].PhysicalPath, rootPath)
            || !MediaFileTypes.IsVideo(files[0].FileName))
            return files;

        var parentPath = Path.GetDirectoryName(rootPath);
        if (string.IsNullOrEmpty(parentPath)) return files;

        var siblings = new List<FileStoreInfo>();
        await foreach (var entry in store.EnumerateDirectory(parentPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsDirectory) siblings.Add(entry);
        }

        var videoNames = siblings
            .Where(entry => MediaFileTypes.IsVideo(entry.FileName))
            .Select(entry => entry.FileName)
            .ToList();
        var result = new List<DiscoveredFile>(files);
        result.AddRange(siblings
            .Where(entry => MediaFileTypes.IsSubtitle(entry.FileName)
                            && string.Equals(
                                MediaFileTypes.FindBestVideoForSubtitle(
                                    videoNames,
                                    entry.FileName),
                                files[0].FileName,
                                StringComparison.OrdinalIgnoreCase))
            .Select(entry => new DiscoveredFile(
                entry.Path,
                entry.FileName,
                entry.FileName)));

        return result
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<List<DiscoveredFile>> EnumerateFilesAsync(
        IFileStore store, string rootPath, CancellationToken cancellationToken)
    {
        var result = new List<DiscoveredFile>();
        await WalkAsync(store, rootPath, "", result, cancellationToken);
        return result
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        static async Task WalkAsync(
            IFileStore store, string path, string relativeBase,
            List<DiscoveredFile> accumulator, CancellationToken cancellationToken)
        {
            await foreach (var entry in store.EnumerateDirectory(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = string.IsNullOrEmpty(relativeBase)
                    ? entry.FileName
                    : relativeBase + "/" + entry.FileName;
                if (entry.IsDirectory)
                    await WalkAsync(store, entry.Path, relative, accumulator, cancellationToken);
                else
                    accumulator.Add(new DiscoveredFile(entry.Path, entry.FileName, relative));
            }
        }
    }

    private async Task<List<FileMapping>> BuildMappingsAsync(
        AnimationInfo info,
        List<DiscoveredFile> files,
        bool forceAi,
        bool allowAi,
        List<string>? warnings,
        CancellationToken cancellationToken)
    {
        var mappings = new List<FileMapping>();
        var reservedPaths = new HashSet<string>(StringComparer.Ordinal);

        var animationName = info.Animation is not null
            ? SanitizePathSegment(info.Animation.Name)
            : null;
        var subGroup = SanitizePathSegment(info.Group?.Name ?? "Unknown");
        var season = info.Season;
        var knownRoot = animationName is not null && season is not null
            ? $"/{animationName}/{subGroup}"
            : null;
        var unknownRoot = GetUnknownRoot(info);

        // Case 1: unknown anime — everything goes under /unknown preserving tree
        if (knownRoot is null)
        {
            foreach (var file in files)
            {
                var virtualPath = $"{unknownRoot}/{file.RelativePath}";
                AddMapping(mappings, reservedPaths, info, file, virtualPath, cancellationToken);
            }
            return await ResolveCollisionsAsync(mappings, warnings, cancellationToken);
        }

        var videos = files.Where(f => MediaFileTypes.VideoExtensions.Contains(Path.GetExtension(f.FileName))).ToList();
        var subtitles = files.Where(f => MediaFileTypes.SubtitleExtensions.Contains(Path.GetExtension(f.FileName))).ToList();
        var others = files.Except(videos).Except(subtitles).ToList();

        // Case 2: known single-episode — pick largest video
        if (info.Episode is { } episode)
        {
            if (videos.Count == 0)
            {
                // Degrade to unknown rule entirely
                foreach (var file in files)
                    AddMapping(mappings, reservedPaths, info, file,
                        $"{unknownRoot}/{file.RelativePath}", cancellationToken);
                return await ResolveCollisionsAsync(mappings, warnings, cancellationToken);
            }

            var mainVideo = videos.Count == 1
                ? videos[0]
                : videos.OrderByDescending(f => SafeFileLength(f.PhysicalPath)).First();

            var ext = Path.GetExtension(mainVideo.FileName);
            var baseName = $"{animationName} S{season:D2}E{episode:D2}";
            AddMapping(mappings, reservedPaths, info, mainVideo,
                $"{knownRoot}/{baseName}{ext}", cancellationToken);

            var matchedSubtitles = new HashSet<DiscoveredFile>();
            foreach (var subtitle in MatchSubtitles(
                         mainVideo,
                         videos,
                         subtitles,
                         allowDirectoryFallback: videos.Count == 1))
            {
                matchedSubtitles.Add(subtitle.File);
                AddMapping(mappings, reservedPaths, info, subtitle.File,
                    $"{knownRoot}/{baseName}{subtitle.Suffix}{Path.GetExtension(subtitle.File.FileName)}",
                    cancellationToken);
            }

            foreach (var video in videos.Where(v => v != mainVideo))
                AddMapping(mappings, reservedPaths, info, video,
                    $"{unknownRoot}/{video.RelativePath}", cancellationToken);
            foreach (var sub in subtitles.Where(s => !matchedSubtitles.Contains(s)))
                AddMapping(mappings, reservedPaths, info, sub,
                    $"{unknownRoot}/{sub.RelativePath}", cancellationToken);
            foreach (var file in others)
                AddMapping(mappings, reservedPaths, info, file,
                    $"{unknownRoot}/{file.RelativePath}", cancellationToken);

            return await ResolveCollisionsAsync(mappings, warnings, cancellationToken);
        }

        // Case 3: known multi-episode — resolve the whole video batch. Stored rules
        // run first during normal mapping; only unresolved files are sent to AI.
        var inferredFiles = await InferVideoFilesAsync(
            info,
            videos,
            forceAi,
            allowAi,
            cancellationToken);
        var matchedSubs = new HashSet<DiscoveredFile>();
        foreach (var video in videos)
        {
            if (!inferredFiles.TryGetValue(video.RelativePath, out var inference))
            {
                LogCouldNotInferEpisode(logger, video.FileName);
                AddMapping(mappings, reservedPaths, info, video,
                    $"{unknownRoot}/{video.RelativePath}", cancellationToken);
                continue;
            }

            var ext = Path.GetExtension(video.FileName);
            var inferredSeason = inference.Season ?? season!.Value;
            var baseName = $"{animationName} S{inferredSeason:D2}E{inference.Episode:D2}";
            AddMapping(mappings, reservedPaths, info, video,
                $"{knownRoot}/{baseName}{ext}", cancellationToken);

            foreach (var subtitle in MatchSubtitles(
                         video,
                         videos,
                         subtitles,
                         allowDirectoryFallback: false))
            {
                matchedSubs.Add(subtitle.File);
                AddMapping(mappings, reservedPaths, info, subtitle.File,
                    $"{knownRoot}/{baseName}{subtitle.Suffix}{Path.GetExtension(subtitle.File.FileName)}",
                    cancellationToken);
            }
        }

        foreach (var sub in subtitles.Where(s => !matchedSubs.Contains(s)))
            AddMapping(mappings, reservedPaths, info, sub,
                $"{unknownRoot}/{sub.RelativePath}", cancellationToken);
        foreach (var file in others)
            AddMapping(mappings, reservedPaths, info, file,
                $"{unknownRoot}/{file.RelativePath}", cancellationToken);

        return await ResolveCollisionsAsync(mappings, warnings, cancellationToken);
    }

    private async Task<Dictionary<string, FileNameInferenceResult>> InferVideoFilesAsync(
        AnimationInfo info,
        IReadOnlyList<DiscoveredFile> videos,
        bool forceAi,
        bool allowAi,
        CancellationToken cancellationToken)
    {
        var inputs = videos
            .Select(video => new FileNameInferenceInput(video.RelativePath, video.FileName))
            .ToList();
        var results = new Dictionary<string, FileNameInferenceResult>(StringComparer.Ordinal);

        if (!forceAi)
            await ApplyRegexRulesAsync(info.Animation!.Id, inputs, results, cancellationToken);

        var unresolved = inputs
            .Where(input => !results.ContainsKey(input.FilePath))
            .ToList();

        if (allowAi && unresolved.Count > 0 && inferenceEngine is not null)
        {
            var aiResults = await inferenceEngine.InferFileNamesAsync(
                new FileNameInferenceRequest(
                    info.Animation!.Id,
                    info.Title,
                    inputs,
                    AllowRegexRuleCreation: !forceAi,
                    TargetFilePaths: unresolved.Select(input => input.FilePath).ToList(),
                    ExistingResults: results.Values
                        .Select(result => result with { Season = result.Season ?? info.Season })
                        .ToList(),
                    DefaultSeason: info.Season),
                cancellationToken);

            foreach (var result in aiResults)
            {
                if (unresolved.Any(input => input.FilePath == result.FilePath))
                    results[result.FilePath] = result;
            }

            // The AI may have saved a valid rule but returned malformed final JSON.
            // Re-read the library so the validated tool result can still resolve files.
            if (!forceAi && results.Count < inputs.Count)
                await ApplyRegexRulesAsync(info.Animation.Id, inputs, results, cancellationToken);
        }

        return results;
    }

    private async Task ApplyRegexRulesAsync(
        Guid animationId,
        IReadOnlyList<FileNameInferenceInput> inputs,
        IDictionary<string, FileNameInferenceResult> results,
        CancellationToken cancellationToken)
    {
        var rules = await fileNameRegexRuleRepository.GetForAnimationAsync(animationId, cancellationToken);
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FileNameRegexMatcher.TryCreateRegex(rule.Pattern, out var regex, out var error))
            {
                LogInvalidRegexRule(logger, rule.Id, error ?? "Unknown validation error");
                continue;
            }

            foreach (var input in inputs)
            {
                if (results.ContainsKey(input.FilePath)) continue;
                var match = FileNameRegexMatcher.Match(regex!, input);
                if (match is not null) results[input.FilePath] = match;
            }

            if (results.Count == inputs.Count) break;
        }
    }

    private sealed record SubtitleMatch(DiscoveredFile File, string Suffix);

    private static IEnumerable<SubtitleMatch> MatchSubtitles(
        DiscoveredFile video,
        IReadOnlyList<DiscoveredFile> videos,
        List<DiscoveredFile> subtitles,
        bool allowDirectoryFallback)
    {
        var videoBase = Path.GetFileNameWithoutExtension(video.FileName);
        var videoDirectory = Path.GetDirectoryName(video.RelativePath) ?? string.Empty;
        var candidates = subtitles
            .Where(subtitle => FindBestVideoForSubtitle(subtitle, videos) == video)
            .ToList();

        if (allowDirectoryFallback)
        {
            candidates = candidates
                .Concat(subtitles
                .Where(sub => string.Equals(
                    Path.GetDirectoryName(sub.RelativePath) ?? string.Empty,
                    videoDirectory,
                    StringComparison.Ordinal)))
                .Distinct()
                .ToList();
        }

        return candidates.Select(sub =>
        {
            var subBase = Path.GetFileNameWithoutExtension(sub.FileName);
            var suffix = subBase.StartsWith(videoBase, StringComparison.OrdinalIgnoreCase)
                ? subBase[videoBase.Length..]
                : $".{subBase}";
            return new SubtitleMatch(sub, suffix);
        });
    }

    private static DiscoveredFile? FindBestVideoForSubtitle(
        DiscoveredFile subtitle,
        IReadOnlyList<DiscoveredFile> videos)
    {
        var subtitleDirectory = Path.GetDirectoryName(subtitle.RelativePath) ?? string.Empty;
        var namedMatches = videos
            .Where(video => MediaFileTypes.IsSubtitleFor(video.FileName, subtitle.FileName))
            .ToList();
        var sameDirectoryMatches = namedMatches
            .Where(video => string.Equals(
                Path.GetDirectoryName(video.RelativePath) ?? string.Empty,
                subtitleDirectory,
                StringComparison.Ordinal))
            .ToList();
        var candidates = sameDirectoryMatches.Count > 0
            ? sameDirectoryMatches
            : namedMatches;
        if (candidates.Count == 0) return null;

        var longestStem = candidates.Max(candidate =>
            Path.GetFileNameWithoutExtension(candidate.FileName).Length);
        var best = candidates
            .Where(candidate =>
                Path.GetFileNameWithoutExtension(candidate.FileName).Length == longestStem)
            .ToList();
        return best.Count == 1 ? best[0] : null;
    }

    private static void AddMapping(
        List<FileMapping> mappings,
        HashSet<string> reservedPaths,
        AnimationInfo info,
        DiscoveredFile file,
        string virtualPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Apply in-memory collision suffix first so the batch does not conflict with itself.
        var resolved = ApplySuffixUntilUnique(virtualPath, reservedPaths.Contains);
        reservedPaths.Add(resolved);
        mappings.Add(new FileMapping(
            Guid.NewGuid(),
            info.Id,
            resolved,
            file.PhysicalPath,
            info.FileStore!));
    }

    private async Task<List<FileMapping>> ResolveCollisionsAsync(
        List<FileMapping> mappings,
        List<string>? warnings,
        CancellationToken cancellationToken)
    {
        var result = new List<FileMapping>(mappings.Count);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            var path = mapping.VirtualPath;
            while (taken.Contains(path) || await IsTakenByAnotherDownloadAsync(
                       path,
                       mapping.AnimationInfoId,
                       cancellationToken))
                path = NextSuffixed(path);
            if (!string.Equals(path, mapping.VirtualPath, StringComparison.Ordinal))
                warnings?.Add("collisionAdjusted");
            taken.Add(path);
            result.Add(mapping with { VirtualPath = path });
        }
        return result;
    }

    private async Task<bool> IsTakenByAnotherDownloadAsync(
        string virtualPath,
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        var existing = await fileMappingRepository.FindByVirtualPathAsync(virtualPath, cancellationToken);
        return existing is not null && existing.AnimationInfoId != animationInfoId;
    }

    private static string ApplySuffixUntilUnique(string virtualPath, Func<string, bool> isTaken)
    {
        if (!isTaken(virtualPath)) return virtualPath;
        var next = virtualPath;
        while (isTaken(next)) next = NextSuffixed(next);
        return next;
    }

    private static string NextSuffixed(string virtualPath)
    {
        var lastSlash = virtualPath.LastIndexOf('/');
        var dir = lastSlash >= 0 ? virtualPath[..lastSlash] : "";
        var fileName = lastSlash >= 0 ? virtualPath[(lastSlash + 1)..] : virtualPath;
        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[dot..] : "";

        // Existing " (n)" suffix?
        if (stem.Length > 4 && stem[^1] == ')')
        {
            var open = stem.LastIndexOf('(');
            if (open > 0 && stem[open - 1] == ' ')
            {
                var numberPart = stem[(open + 1)..^1];
                if (int.TryParse(numberPart, out var current))
                {
                    var newStem = stem[..(open + 1)] + (current + 1) + ")";
                    return (dir.Length > 0 ? dir + "/" : "/") + newStem + ext;
                }
            }
        }
        return (dir.Length > 0 ? dir + "/" : "/") + stem + " (2)" + ext;
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static string GetUnknownRoot(AnimationInfo info)
    {
        if (!string.Equals(
                info.DownloadType,
                FileDownloadTypes.MediaLibraryImport,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(info.StorePath))
            return UnknownRoot;

        var trimmedPath = Path.TrimEndingDirectorySeparator(info.StorePath);
        var itemName = MediaFileTypes.IsVideo(trimmedPath)
            ? Path.GetFileNameWithoutExtension(trimmedPath)
            : Path.GetFileName(trimmedPath);
        return $"{UnknownRoot}/{SanitizePathSegment(itemName)}";
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AnimationInfo {ItemId} not found, skipping mapping")]
    private static partial void LogAnimationInfoNotFound(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AnimationInfo {ItemId} has no store path, skipping mapping")]
    private static partial void LogNoStorePath(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No file store registered for {FileStore}")]
    private static partial void LogNoFileStore(ILogger logger, string fileStore);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No files found under {StorePath}")]
    private static partial void LogNoFiles(ILogger logger, string storePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not infer episode for file: {FileName}")]
    private static partial void LogCouldNotInferEpisode(ILogger logger, string fileName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mapped {Count} files for {ItemId}")]
    private static partial void LogMapped(ILogger logger, Guid itemId, int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Forced AI filename inference did not identify every video for AnimationInfo {ItemId}; existing mappings were preserved")]
    private static partial void LogForcedInferenceNoResult(ILogger logger, Guid itemId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Skipping invalid filename regex rule {RuleId}: {Error}")]
    private static partial void LogInvalidRegexRule(ILogger logger, Guid ruleId, string error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Download state changed while mapping AnimationInfo {ItemId}; generated mappings were discarded")]
    private static partial void LogDownloadChangedDuringMapping(ILogger logger, Guid itemId);
}
