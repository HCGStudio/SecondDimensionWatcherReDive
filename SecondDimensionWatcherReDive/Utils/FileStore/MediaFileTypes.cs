namespace SecondDimensionWatcherReDive.Utils.FileStore;

internal static class MediaFileTypes
{
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".flv", ".wmv", ".webm", ".mov", ".m4v", ".ts", ".m2ts"
    };

    public static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt"
    };

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static bool IsSubtitle(string path) => SubtitleExtensions.Contains(Path.GetExtension(path));

    public static bool IsSubtitleFor(string videoFileName, string subtitleFileName) =>
        IsSubtitle(subtitleFileName)
        && IsSubtitleStemMatch(
            Path.GetFileNameWithoutExtension(videoFileName),
            Path.GetFileNameWithoutExtension(subtitleFileName));

    public static string? FindBestVideoForSubtitle(
        IEnumerable<string> videoFileNames,
        string subtitleFileName)
    {
        if (!IsSubtitle(subtitleFileName)) return null;

        var matches = videoFileNames
            .Where(videoFileName => IsSubtitleFor(videoFileName, subtitleFileName))
            .Select(videoFileName => new
            {
                Name = videoFileName,
                StemLength = Path.GetFileNameWithoutExtension(videoFileName).Length
            })
            .ToList();
        if (matches.Count == 0) return null;

        var longest = matches.Max(match => match.StemLength);
        var best = matches
            .Where(match => match.StemLength == longest)
            .Select(match => match.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return best.Count == 1 ? best[0] : null;
    }

    public static bool IsSubtitleStemMatch(string videoBase, string subtitleBase)
    {
        if (subtitleBase.Equals(videoBase, StringComparison.OrdinalIgnoreCase)) return true;
        if (!subtitleBase.StartsWith(videoBase, StringComparison.OrdinalIgnoreCase)
            || subtitleBase.Length == videoBase.Length)
            return false;
        return subtitleBase[videoBase.Length] is '.' or ' ' or '_' or '-' or '[' or '(';
    }
}
