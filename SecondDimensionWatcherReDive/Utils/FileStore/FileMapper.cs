using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public interface IFileMapper
{
    Task MapDownloadAsync(Guid animationInfoId, CancellationToken cancellationToken);

    Task<bool> ReidentifyFilesWithAiAsync(Guid animationInfoId, CancellationToken cancellationToken);
}

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

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".flv", ".wmv", ".webm"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt"
    };

    public async Task MapDownloadAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        _ = await MapDownloadCoreAsync(animationInfoId, false, cancellationToken);
    }

    public Task<bool> ReidentifyFilesWithAiAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        if (inferenceEngine is null)
            throw new AiFileNameInferenceUnavailableException();

        return MapDownloadCoreAsync(animationInfoId, true, cancellationToken);
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

        if (info.FileStore is null || info.StorePath is null)
        {
            LogNoStorePath(logger, animationInfoId);
            return false;
        }

        var store = fileStoreProvider.GetClient(info.FileStore);
        if (store is null)
        {
            LogNoFileStore(logger, info.FileStore);
            return false;
        }

        var files = await EnumerateFilesAsync(store, info.StorePath, cancellationToken);
        if (files.Count == 0)
        {
            LogNoFiles(logger, info.StorePath);
            return false;
        }

        var mappings = await BuildMappingsAsync(info, files, forceAi, cancellationToken);
        if (mappings.Count == 0) return false;

        // A manual AI-only retry is all-or-nothing. Partial results must not replace a
        // previously complete mapping and move the unresolved episodes to /unknown.
        if (forceAi && !AllVideoFilesIdentified(files, mappings))
        {
            LogForcedInferenceNoResult(logger, animationInfoId);
            return false;
        }

        // Idempotent: replace any prior mappings for this AnimationInfo so re-runs
        // (e.g. after post-download inference fills in the canonical path) don't
        // collide with the unique VirtualPath index.
        var replaced = await fileMappingRepository.ReplaceForAnimationInfoAsync(
            info.Id,
            info.FileStore,
            info.StorePath,
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

    private static bool AllVideoFilesIdentified(
        IReadOnlyList<DiscoveredFile> files,
        IReadOnlyList<FileMapping> mappings)
    {
        var videos = files
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file.FileName)))
            .ToList();
        if (videos.Count == 0) return false;

        foreach (var video in videos)
        {
            var mapping = mappings.FirstOrDefault(candidate =>
                candidate.PhysicalPath == video.PhysicalPath);
            if (mapping is null
                || mapping.VirtualPath.StartsWith(UnknownRoot + "/", StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private sealed record DiscoveredFile(string PhysicalPath, string FileName, string RelativePath);

    private static async Task<List<DiscoveredFile>> EnumerateFilesAsync(
        IFileStore store, string rootPath, CancellationToken cancellationToken)
    {
        var result = new List<DiscoveredFile>();
        await WalkAsync(store, rootPath, "", result, cancellationToken);
        return result;

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

        // Case 1: unknown anime — everything goes under /unknown preserving tree
        if (knownRoot is null)
        {
            foreach (var file in files)
            {
                var virtualPath = $"{UnknownRoot}/{file.RelativePath}";
                AddMapping(mappings, reservedPaths, info, file, virtualPath, cancellationToken);
            }
            return await ResolveCollisionsAsync(mappings, cancellationToken);
        }

        var videos = files.Where(f => VideoExtensions.Contains(Path.GetExtension(f.FileName))).ToList();
        var subtitles = files.Where(f => SubtitleExtensions.Contains(Path.GetExtension(f.FileName))).ToList();
        var others = files.Except(videos).Except(subtitles).ToList();

        // Case 2: known single-episode — pick largest video
        if (info.Episode is { } episode)
        {
            if (videos.Count == 0)
            {
                // Degrade to unknown rule entirely
                foreach (var file in files)
                    AddMapping(mappings, reservedPaths, info, file,
                        $"{UnknownRoot}/{file.RelativePath}", cancellationToken);
                return await ResolveCollisionsAsync(mappings, cancellationToken);
            }

            var mainVideo = videos.Count == 1
                ? videos[0]
                : videos.OrderByDescending(f => SafeFileLength(f.PhysicalPath)).First();

            var ext = Path.GetExtension(mainVideo.FileName);
            var baseName = $"{animationName} S{season:D2}E{episode:D2}";
            AddMapping(mappings, reservedPaths, info, mainVideo,
                $"{knownRoot}/{baseName}{ext}", cancellationToken);

            var matchedSubtitles = new HashSet<DiscoveredFile>();
            foreach (var subtitle in MatchSubtitles(mainVideo.FileName, subtitles, out var suffixes))
            {
                matchedSubtitles.Add(subtitle.File);
                AddMapping(mappings, reservedPaths, info, subtitle.File,
                    $"{knownRoot}/{baseName}{subtitle.Suffix}{Path.GetExtension(subtitle.File.FileName)}",
                    cancellationToken);
                _ = suffixes;
            }

            foreach (var video in videos.Where(v => v != mainVideo))
                AddMapping(mappings, reservedPaths, info, video,
                    $"{UnknownRoot}/{video.RelativePath}", cancellationToken);
            foreach (var sub in subtitles.Where(s => !matchedSubtitles.Contains(s)))
                AddMapping(mappings, reservedPaths, info, sub,
                    $"{UnknownRoot}/{sub.RelativePath}", cancellationToken);
            foreach (var file in others)
                AddMapping(mappings, reservedPaths, info, file,
                    $"{UnknownRoot}/{file.RelativePath}", cancellationToken);

            return await ResolveCollisionsAsync(mappings, cancellationToken);
        }

        // Case 3: known multi-episode — resolve the whole video batch. Stored rules
        // run first during normal mapping; only unresolved files are sent to AI.
        var inferredFiles = await InferVideoFilesAsync(info, videos, forceAi, cancellationToken);
        var matchedSubs = new HashSet<DiscoveredFile>();
        foreach (var video in videos)
        {
            if (!inferredFiles.TryGetValue(video.RelativePath, out var inference))
            {
                LogCouldNotInferEpisode(logger, video.FileName);
                AddMapping(mappings, reservedPaths, info, video,
                    $"{UnknownRoot}/{video.RelativePath}", cancellationToken);
                continue;
            }

            var ext = Path.GetExtension(video.FileName);
            var inferredSeason = inference.Season ?? season!.Value;
            var baseName = $"{animationName} S{inferredSeason:D2}E{inference.Episode:D2}";
            AddMapping(mappings, reservedPaths, info, video,
                $"{knownRoot}/{baseName}{ext}", cancellationToken);

            foreach (var subtitle in MatchSubtitles(video.FileName, subtitles, out _))
            {
                matchedSubs.Add(subtitle.File);
                AddMapping(mappings, reservedPaths, info, subtitle.File,
                    $"{knownRoot}/{baseName}{subtitle.Suffix}{Path.GetExtension(subtitle.File.FileName)}",
                    cancellationToken);
            }
        }

        foreach (var sub in subtitles.Where(s => !matchedSubs.Contains(s)))
            AddMapping(mappings, reservedPaths, info, sub,
                $"{UnknownRoot}/{sub.RelativePath}", cancellationToken);
        foreach (var file in others)
            AddMapping(mappings, reservedPaths, info, file,
                $"{UnknownRoot}/{file.RelativePath}", cancellationToken);

        return await ResolveCollisionsAsync(mappings, cancellationToken);
    }

    private async Task<Dictionary<string, FileNameInferenceResult>> InferVideoFilesAsync(
        AnimationInfo info,
        IReadOnlyList<DiscoveredFile> videos,
        bool forceAi,
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

        if (unresolved.Count > 0 && inferenceEngine is not null)
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
        string videoFileName, List<DiscoveredFile> subtitles, out List<string> suffixes)
    {
        var videoBase = Path.GetFileNameWithoutExtension(videoFileName);
        var matches = new List<SubtitleMatch>();
        suffixes = new List<string>();
        foreach (var sub in subtitles)
        {
            var subBase = Path.GetFileNameWithoutExtension(sub.FileName);
            if (!subBase.Equals(videoBase, StringComparison.OrdinalIgnoreCase)
                && !subBase.StartsWith(videoBase + ".", StringComparison.OrdinalIgnoreCase))
                continue;
            var suffix = subBase.Length > videoBase.Length ? subBase[videoBase.Length..] : "";
            matches.Add(new SubtitleMatch(sub, suffix));
            suffixes.Add(suffix);
        }
        return matches;
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
        List<FileMapping> mappings, CancellationToken cancellationToken)
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
