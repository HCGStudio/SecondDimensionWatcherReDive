using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.Authorization;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using DataAnimationInfo = SecondDimensionWatcherReDive.Framework.DataRepository.AnimationInfo;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/playback")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed partial class PlaybackController(
    IPlaybackRepository playbackRepository,
    IAnimationInfoRepository animationInfoRepository,
    IFileMappingRepository fileMappingRepository) : ControllerBase
{
    private const int DefaultContinueLimit = 20;
    private const int MaxContinueLimit = 100;
    private const double WatchedThreshold = 0.9d;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".webm", ".avi", ".flv", ".wmv", ".mov", ".m4v", ".ts", ".m2ts"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ass", ".ssa", ".srt", ".vtt"
    };

    [HttpGet("continue")]
    public async Task<IActionResult> ContinueWatching(
        [FromQuery, Range(1, MaxContinueLimit)] int limit = DefaultContinueLimit,
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();

        var items = await playbackRepository.GetContinueWatchingAsync(userId, limit, cancellationToken);
        var response = items
            .Select(item => new External.ContinueWatchingResponse(
                ToMediaResponse(item.Media),
                ToStateResponse(item.Progress, item.Media.Path)))
            .ToArray();
        return Ok(response);
    }

    [HttpGet("states")]
    public async Task<IActionResult> GetStates(
        [FromQuery] Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();
        if (animationInfoId == Guid.Empty) return BadRequest();

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(animationInfoId, cancellationToken);
        if (info is null) return NotFound();

        var mappings = await fileMappingRepository.GetForAnimationInfoAsync(animationInfoId, cancellationToken);
        var states = await playbackRepository.GetStatesAsync(userId, animationInfoId, cancellationToken);
        var stateByPath = states.ToDictionary(state => state.VirtualPath, StringComparer.Ordinal);
        var response = mappings
            .Where(mapping => IsVideo(mapping.VirtualPath)
                              && IsAddressable(info, mapping.VirtualPath))
            .OrderBy(mapping => mapping.VirtualPath, StringComparer.Ordinal)
            .Select(mapping => stateByPath.TryGetValue(mapping.VirtualPath, out var state)
                ? ToStateResponse(state, GetRelativePath(info, mapping.VirtualPath))
                : new External.PlaybackStateResponse(
                    animationInfoId,
                    GetRelativePath(info, mapping.VirtualPath),
                    mapping.VirtualPath,
                    0,
                    0,
                    false,
                    null,
                    null))
            .ToArray();
        return Ok(response);
    }

    [HttpGet("context")]
    public async Task<IActionResult> GetContext(
        [FromQuery] Guid animationInfoId,
        [FromQuery, Required] string? path,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();
        var resolution = await ResolveVideoAsync(animationInfoId, path, cancellationToken);
        if (resolution.Status is ResolutionStatus.Invalid) return BadRequest();
        if (resolution.Status is ResolutionStatus.Missing) return NotFound();

        var info = resolution.Info!;
        var mapping = resolution.Mapping!;
        var state = await playbackRepository.FindProgressAsync(
            userId, info.Id, mapping.VirtualPath, cancellationToken);
        var preferences = await playbackRepository.GetPreferencesAsync(userId, cancellationToken);
        var next = await playbackRepository.GetNextMediaAsync(
            info.Id, mapping.VirtualPath, cancellationToken);
        var mappings = await fileMappingRepository.GetForAnimationInfoAsync(info.Id, cancellationToken);

        var media = CreateCurrentMedia(info, mapping.VirtualPath, resolution.RelativePath!);
        var response = new External.PlaybackContextResponse(
            ToMediaResponse(media),
            state is null ? null : ToStateResponse(state, resolution.RelativePath!),
            ToPreferencesResponse(preferences),
            AssociateSubtitles(info, mapping.VirtualPath, mappings),
            next is null ? null : ToMediaResponse(next));
        return Ok(response);
    }

    [HttpPut("progress")]
    [Authorize(Policy = AccessPolicies.PlaybackWrite)]
    public async Task<IActionResult> UpdateProgress(
        [FromBody] External.PlaybackProgressRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();
        if (!double.IsFinite(request.PositionSeconds)
            || !double.IsFinite(request.DurationSeconds)
            || request.PositionSeconds < 0
            || request.DurationSeconds < 0)
            return BadRequest();

        var resolution = await ResolveVideoAsync(request.AnimationInfoId, request.Path, cancellationToken);
        if (resolution.Status is ResolutionStatus.Invalid) return BadRequest();
        if (resolution.Status is ResolutionStatus.Missing) return NotFound();

        var duration = request.DurationSeconds;
        var position = duration > 0
            ? Math.Min(request.PositionSeconds, duration)
            : request.PositionSeconds;
        var markWatched = duration > 0 && position / duration >= WatchedThreshold;
        PlaybackProgress progress;
        try
        {
            progress = await playbackRepository.UpsertProgressAsync(
                userId,
                request.AnimationInfoId,
                resolution.Mapping!.VirtualPath,
                position,
                duration,
                markWatched,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (PlaybackMappingChangedException)
        {
            return Conflict();
        }
        return Ok(ToStateResponse(progress, resolution.RelativePath!));
    }

    [HttpPut("watched")]
    [Authorize(Policy = AccessPolicies.PlaybackWrite)]
    public async Task<IActionResult> SetWatched(
        [FromBody] External.PlaybackWatchedRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();

        var resolution = await ResolveVideoAsync(request.AnimationInfoId, request.Path, cancellationToken);
        if (resolution.Status is ResolutionStatus.Invalid) return BadRequest();
        if (resolution.Status is ResolutionStatus.Missing) return NotFound();

        PlaybackProgress progress;
        try
        {
            progress = await playbackRepository.SetWatchedAsync(
                userId,
                request.AnimationInfoId,
                resolution.Mapping!.VirtualPath,
                request.IsWatched,
                DateTimeOffset.UtcNow,
                cancellationToken);
        }
        catch (PlaybackMappingChangedException)
        {
            return Conflict();
        }
        return Ok(ToStateResponse(progress, resolution.RelativePath!));
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();
        var preferences = await playbackRepository.GetPreferencesAsync(userId, cancellationToken);
        return Ok(ToPreferencesResponse(preferences));
    }

    [HttpPut("preferences")]
    [Authorize(Policy = AccessPolicies.PlaybackWrite)]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] External.PlaybackPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetProfileId(out var userId)) return Unauthorized();

        var preferences = new PlaybackPreferences(
            userId,
            NormalizePreference(request.SubtitleLanguage),
            NormalizePreference(request.SubtitleTrackLabel),
            NormalizePreference(request.AudioLanguage),
            NormalizePreference(request.AudioTrackLabel),
            request.AutoPlayNext,
            DateTimeOffset.UtcNow);
        var saved = await playbackRepository.UpsertPreferencesAsync(preferences, cancellationToken);
        return Ok(ToPreferencesResponse(saved));
    }

    private async Task<MediaResolution> ResolveVideoAsync(
        Guid animationInfoId,
        string? path,
        CancellationToken cancellationToken)
    {
        if (animationInfoId == Guid.Empty || !TryNormalizeRelativePath(path, out var relativePath))
            return MediaResolution.Invalid;

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(animationInfoId, cancellationToken);
        if (info is null || !info.IsDownloadFinished) return MediaResolution.Missing;

        var root = GetAnimationVirtualRoot(info);
        var virtualPath = $"{root}/{relativePath}";
        var mapping = await fileMappingRepository.FindByVirtualPathAsync(virtualPath, cancellationToken);
        if (mapping is null
            || mapping.AnimationInfoId != animationInfoId
            || !IsVideo(mapping.VirtualPath))
            return MediaResolution.Missing;

        return new MediaResolution(ResolutionStatus.Found, info, mapping, relativePath);
    }

    private static IReadOnlyList<External.ExternalSubtitleResponse> AssociateSubtitles(
        DataAnimationInfo info,
        string videoVirtualPath,
        IReadOnlyList<FileMapping> mappings)
    {
        var directory = GetDirectory(videoVirtualPath);
        var videoStem = Path.GetFileNameWithoutExtension(videoVirtualPath);
        var videosInDirectory = mappings.Count(mapping =>
            IsVideo(mapping.VirtualPath)
            && string.Equals(GetDirectory(mapping.VirtualPath), directory, StringComparison.Ordinal));

        return mappings
            .Where(mapping => SubtitleExtensions.Contains(Path.GetExtension(mapping.VirtualPath)))
            .Where(mapping => string.Equals(
                GetDirectory(mapping.VirtualPath), directory, StringComparison.Ordinal))
            .Where(mapping => IsSubtitleMatch(
                videoStem,
                Path.GetFileNameWithoutExtension(mapping.VirtualPath),
                videosInDirectory))
            .OrderBy(mapping => mapping.VirtualPath, StringComparer.Ordinal)
            .Select(mapping =>
            {
                var subtitleStem = Path.GetFileNameWithoutExtension(mapping.VirtualPath);
                return new External.ExternalSubtitleResponse(
                    GetRelativePath(info, mapping.VirtualPath),
                    mapping.VirtualPath,
                    InferSubtitleLanguage(videoStem, subtitleStem),
                    Path.GetFileName(mapping.VirtualPath),
                    Path.GetExtension(mapping.VirtualPath).TrimStart('.').ToLowerInvariant());
            })
            .ToArray();
    }

    private static bool IsSubtitleMatch(string videoStem, string subtitleStem, int videosInDirectory)
    {
        if (string.Equals(videoStem, subtitleStem, StringComparison.OrdinalIgnoreCase)) return true;
        if (subtitleStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase))
        {
            var suffix = subtitleStem[videoStem.Length..];
            if (suffix.Length > 0 && IsSubtitleSeparator(suffix[0])) return true;
        }

        // A dedicated episode directory containing one video is an unambiguous association,
        // even when the sidecar uses a generic name such as "Chinese.ass".
        return videosInDirectory == 1;
    }

    private static string? InferSubtitleLanguage(string videoStem, string subtitleStem)
    {
        var suffix = subtitleStem.StartsWith(videoStem, StringComparison.OrdinalIgnoreCase)
            ? subtitleStem[videoStem.Length..]
            : subtitleStem;
        var normalized = suffix.ToLowerInvariant().Replace('_', '-');

        if (ContainsLanguageToken(normalized, "zh-hans", "chs", "sc", "gb")) return "zh-Hans";
        if (ContainsLanguageToken(normalized, "zh-hant", "cht", "tc", "big5")) return "zh-Hant";
        if (ContainsLanguageToken(normalized, "zh", "zho", "chi", "cn", "chinese")) return "zh";
        if (ContainsLanguageToken(normalized, "en", "eng", "english")) return "en";
        if (ContainsLanguageToken(normalized, "ja", "jp", "jpn", "japanese")) return "ja";
        if (ContainsLanguageToken(normalized, "ko", "kor", "korean")) return "ko";
        if (ContainsLanguageToken(normalized, "fr", "fra", "fre")) return "fr";
        if (ContainsLanguageToken(normalized, "de", "deu", "ger")) return "de";
        if (ContainsLanguageToken(normalized, "es", "spa")) return "es";
        return null;
    }

    private static bool ContainsLanguageToken(string value, params string[] expected)
    {
        var tokens = LanguageTokenRegex().Split(value)
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return expected.Any(language => value.Contains(language, StringComparison.OrdinalIgnoreCase)
                                        && (language.Contains('-') || tokens.Contains(language)));
    }

    private static PlaybackMedia CreateCurrentMedia(
        DataAnimationInfo info,
        string virtualPath,
        string relativePath)
    {
        var (parsedSeason, parsedEpisode) = ParseSeasonEpisode(virtualPath);
        return new PlaybackMedia(
            info.Id,
            virtualPath,
            relativePath,
            info.Title,
            info.Animation?.Id,
            info.Animation?.Name,
            info.Animation?.PosterPath,
            info.Group?.Id,
            info.Group?.Name,
            info.Season ?? parsedSeason,
            info.Episode ?? parsedEpisode,
            info.PublishTime);
    }

    private static External.PlaybackMediaResponse ToMediaResponse(PlaybackMedia media) =>
        new(media.AnimationInfoId,
            media.Path,
            media.VirtualPath,
            media.Title,
            media.AnimationName,
            media.PosterPath,
            media.Season,
            media.Episode);

    private static External.PlaybackStateResponse ToStateResponse(
        PlaybackProgress progress,
        string relativePath) =>
        new(progress.AnimationInfoId,
            relativePath,
            progress.VirtualPath,
            progress.PositionSeconds,
            progress.DurationSeconds,
            progress.IsWatched,
            progress.UpdatedAt,
            progress.WatchedAt);

    private static External.PlaybackPreferencesResponse ToPreferencesResponse(PlaybackPreferences preferences) =>
        new(preferences.SubtitleLanguage,
            preferences.SubtitleTrackLabel,
            preferences.AudioLanguage,
            preferences.AudioTrackLabel,
            preferences.AutoPlayNext,
            preferences.UpdatedAt == DateTimeOffset.UnixEpoch ? null : preferences.UpdatedAt);

    private static bool TryNormalizeRelativePath(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Length > 2048
            || raw.StartsWith('/')
            || raw.StartsWith('\\')
            || raw.Contains('\\')
            || raw.Any(character => char.IsControl(character)))
            return false;

        var segments = raw.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..")) return false;

        normalized = string.Join('/', segments);
        return true;
    }

    private static string GetRelativePath(DataAnimationInfo info, string virtualPath)
    {
        var root = GetAnimationVirtualRoot(info);
        return virtualPath.StartsWith(root + "/", StringComparison.Ordinal)
            ? virtualPath[(root.Length + 1)..]
            : virtualPath.TrimStart('/');
    }

    private static string GetAnimationVirtualRoot(DataAnimationInfo info)
    {
        if (info.Animation is null || info.Season is null) return "/unknown";
        return $"/{SanitizePathSegment(info.Animation.Name)}/{SanitizePathSegment(info.Group?.Name ?? "Unknown")}";
    }

    private static bool IsAddressable(DataAnimationInfo info, string virtualPath)
    {
        var root = GetAnimationVirtualRoot(info);
        return virtualPath.StartsWith(root + "/", StringComparison.Ordinal);
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(character =>
            invalid.Contains(character) || character == '/' ? '_' : character)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }

    private static string GetDirectory(string virtualPath)
    {
        var separator = virtualPath.LastIndexOf('/');
        return separator <= 0 ? "/" : virtualPath[..separator];
    }

    private static bool IsVideo(string virtualPath) =>
        VideoExtensions.Contains(Path.GetExtension(virtualPath));

    private static bool IsSubtitleSeparator(char character) =>
        character is '.' or ' ' or '_' or '-' or '[' or '(';

    private static string? NormalizePreference(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (int? Season, int? Episode) ParseSeasonEpisode(string virtualPath)
    {
        var match = SeasonEpisodeRegex().Match(Path.GetFileNameWithoutExtension(virtualPath));
        return match.Success
            ? (int.Parse(match.Groups["season"].Value), int.Parse(match.Groups["episode"].Value))
            : (null, null);
    }

    private enum ResolutionStatus
    {
        Invalid,
        Missing,
        Found
    }

    private sealed record MediaResolution(
        ResolutionStatus Status,
        DataAnimationInfo? Info,
        FileMapping? Mapping,
        string? RelativePath)
    {
        public static MediaResolution Invalid { get; } = new(ResolutionStatus.Invalid, null, null, null);
        public static MediaResolution Missing { get; } = new(ResolutionStatus.Missing, null, null, null);
    }

    [GeneratedRegex(@"[^a-z0-9-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTokenRegex();

    [GeneratedRegex(@"(?i)(?:^|[ ._\-])S(?<season>\d{1,3})E(?<episode>\d{1,4})(?:$|[^0-9])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SeasonEpisodeRegex();
}
