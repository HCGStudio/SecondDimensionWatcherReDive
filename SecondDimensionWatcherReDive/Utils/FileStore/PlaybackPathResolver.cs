using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

internal static class PlaybackPathResolver
{
    public static string ResolveVirtualPath(AnimationInfo info, string? relative)
    {
        var root = GetAnimationVirtualRoot(info);
        if (string.IsNullOrWhiteSpace(relative)) return root;
        var trimmed = relative.Trim('/');
        return string.IsNullOrEmpty(trimmed) ? root : $"{root}/{trimmed}";
    }

    private static string GetAnimationVirtualRoot(AnimationInfo info)
    {
        if (info.Animation is null || info.Season is null) return "/unknown";
        var animationName = SanitizePathSegment(info.Animation.Name);
        var subGroup = SanitizePathSegment(info.Group?.Name ?? "Unknown");
        return $"/{animationName}/{subGroup}";
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }
}
