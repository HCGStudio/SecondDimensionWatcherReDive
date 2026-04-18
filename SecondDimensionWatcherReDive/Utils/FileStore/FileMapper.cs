using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public interface IFileMapper
{
    Task MapDownloadAsync(Guid animationInfoId, CancellationToken cancellationToken);
}

public partial class FileMapper(
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository,
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
        var info = await animationInfoRepository.FindByIdWithAnimationAsync(animationInfoId, cancellationToken);
        if (info is null)
        {
            LogAnimationInfoNotFound(logger, animationInfoId);
            return;
        }

        if (info.FileStore is null || info.StorePath is null)
        {
            LogNoStorePath(logger, animationInfoId);
            return;
        }

        var store = fileStoreProvider.GetClient(info.FileStore);
        if (store is null)
        {
            LogNoFileStore(logger, info.FileStore);
            return;
        }

        var files = await EnumerateFilesAsync(store, info.StorePath, cancellationToken);
        if (files.Count == 0)
        {
            LogNoFiles(logger, info.StorePath);
            return;
        }

        var mappings = await BuildMappingsAsync(info, files, cancellationToken);
        if (mappings.Count == 0) return;

        // Idempotent: replace any prior mappings for this AnimationInfo so re-runs
        // (e.g. after post-download inference fills in the canonical path) don't
        // collide with the unique VirtualPath index.
        await fileMappingRepository.RemoveByAnimationInfoAsync(info.Id, cancellationToken);
        await fileMappingRepository.AddRangeAsync(mappings, cancellationToken);
        LogMapped(logger, animationInfoId, mappings.Count);
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
        AnimationInfo info, List<DiscoveredFile> files, CancellationToken cancellationToken)
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

        // Case 3: known multi-episode — infer per video
        var matchedSubs = new HashSet<DiscoveredFile>();
        foreach (var video in videos)
        {
            int? inferredEpisode = null;
            int inferredSeason = season!.Value;
            if (inferenceEngine is not null)
            {
                var inference = await inferenceEngine.InferAsync(video.FileName, info.Title, cancellationToken);
                if (inference?.Episode is { } ep)
                {
                    inferredEpisode = ep;
                    inferredSeason = inference.Season ?? season.Value;
                }
            }

            if (inferredEpisode is null)
            {
                LogCouldNotInferEpisode(logger, video.FileName);
                AddMapping(mappings, reservedPaths, info, video,
                    $"{UnknownRoot}/{video.RelativePath}", cancellationToken);
                continue;
            }

            var ext = Path.GetExtension(video.FileName);
            var baseName = $"{animationName} S{inferredSeason:D2}E{inferredEpisode:D2}";
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
            while (taken.Contains(path)
                   || await fileMappingRepository.VirtualPathExistsAsync(path, cancellationToken))
                path = NextSuffixed(path);
            taken.Add(path);
            result.Add(mapping with { VirtualPath = path });
        }
        return result;
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
}
