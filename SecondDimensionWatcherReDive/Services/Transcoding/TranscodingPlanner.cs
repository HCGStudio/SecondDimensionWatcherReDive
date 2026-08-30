namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal static class TranscodingPlanner
{
    private static readonly HashSet<string> TextSubtitleCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "ass", "jacosub", "microdvd", "mov_text", "mpl2", "realtext", "sami", "ssa",
        "subrip", "subviewer", "subviewer1", "text", "vplayer", "webvtt"
    };

    private static readonly HashSet<string> HlsAudioCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "mp3"
    };

    public static TranscodingPlan CreatePlan(
        TranscodingSource source,
        MediaProbe probe,
        TranscodingSelection selection,
        bool burnBitmapSubtitles)
    {
        var video = probe.Video
                    ?? throw new InvalidOperationException("The selected file has no video track.");
        var audio = SelectStream(
            probe.Streams.Where(stream => stream.CodecType == "audio"),
            selection.AudioLanguage,
            selection.AudioTrackLabel);
        var subtitles = probe.Streams.Where(stream => stream.CodecType == "subtitle").ToArray();
        var textSubtitles = subtitles.Where(stream => TextSubtitleCodecs.Contains(stream.CodecName)).ToArray();
        var bitmapSubtitles = subtitles.Where(stream => !TextSubtitleCodecs.Contains(stream.CodecName)).ToArray();
        var hasSubtitlePreference = selection.SubtitleLanguage is not null
                                    || selection.SubtitleTrackLabel is not null;
        var bitmapToBurn = burnBitmapSubtitles
                           && selection.SubtitleLanguage != "off"
            ? SelectStream(
                bitmapSubtitles,
                selection.SubtitleLanguage,
                selection.SubtitleTrackLabel,
                fallbackToDefault: !hasSubtitlePreference)
            : null;

        var extension = Path.GetExtension(source.FileName);
        var copyVideo = IsBrowserCompatibleH264(video)
                        && selection.Quality == "auto"
                        && bitmapToBurn is null;
        var copyAudio = audio is null || HlsAudioCodecs.Contains(audio.CodecName);
        var direct = IsDirectPlayContainer(extension, video, audio?.CodecName)
                     && selection.Quality == "auto"
                     && bitmapToBurn is null;
        var strategy = direct
            ? TranscodingStrategy.Direct
            : copyVideo && copyAudio
                ? TranscodingStrategy.Remux
                : TranscodingStrategy.Transcode;

        return new TranscodingPlan(
            strategy,
            video,
            audio,
            bitmapToBurn,
            textSubtitles,
            bitmapSubtitles.Length - (bitmapToBurn is null ? 0 : 1),
            copyVideo,
            copyAudio);
    }

    private static MediaStreamProbe? SelectStream(
        IEnumerable<MediaStreamProbe> streams,
        string? preferredLanguage,
        string? preferredLabel,
        bool fallbackToDefault = true)
    {
        var candidates = streams.ToArray();
        if (preferredLabel is not null)
        {
            var labelMatch = candidates.FirstOrDefault(stream =>
                string.Equals(stream.Title, preferredLabel, StringComparison.OrdinalIgnoreCase));
            if (labelMatch is not null) return labelMatch;
        }

        if (preferredLanguage is not null)
        {
            var languageMatch = candidates.FirstOrDefault(stream =>
                LanguagesMatch(stream.Language, preferredLanguage));
            if (languageMatch is not null) return languageMatch;
        }

        return fallbackToDefault
            ? candidates.FirstOrDefault(stream => stream.IsDefault) ?? candidates.FirstOrDefault()
            : null;
    }

    private static bool LanguagesMatch(string? actual, string preferred)
    {
        if (actual is null) return false;
        var normalizedActual = NormalizeLanguage(actual);
        return normalizedActual == NormalizeLanguage(preferred);
    }

    private static string NormalizeLanguage(string language)
    {
        var normalized = language.Trim().ToLowerInvariant().Replace('_', '-');
        if (normalized is "chi" or "zho" || normalized.StartsWith("zh-", StringComparison.Ordinal)) return "zh";
        if (normalized is "jpn" || normalized.StartsWith("ja-", StringComparison.Ordinal)) return "ja";
        if (normalized is "eng" || normalized.StartsWith("en-", StringComparison.Ordinal)) return "en";
        var separator = normalized.IndexOf('-');
        return separator < 0 ? normalized : normalized[..separator];
    }

    private static bool IsDirectPlayContainer(
        string extension,
        MediaStreamProbe video,
        string? audioCodec)
    {
        if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase))
            return IsBrowserCompatibleH264(video)
                   && (audioCodec is null || audioCodec.Equals("aac", StringComparison.OrdinalIgnoreCase));

        if (!extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)) return false;
        var supportedVideo = video.CodecName.Equals("vp8", StringComparison.OrdinalIgnoreCase)
                             || video.CodecName.Equals("vp9", StringComparison.OrdinalIgnoreCase)
                             || video.CodecName.Equals("av1", StringComparison.OrdinalIgnoreCase);
        var supportedAudio = audioCodec is null
                             || audioCodec.Equals("opus", StringComparison.OrdinalIgnoreCase)
                             || audioCodec.Equals("vorbis", StringComparison.OrdinalIgnoreCase);
        return supportedVideo && supportedAudio;
    }

    private static bool IsBrowserCompatibleH264(MediaStreamProbe video)
    {
        if (!video.CodecName.Equals("h264", StringComparison.OrdinalIgnoreCase)) return false;

        var profile = video.Profile?.Trim();
        var compatibleProfile = profile is not null
                                && (profile.Equals("Baseline", StringComparison.OrdinalIgnoreCase)
                                    || profile.Equals("Constrained Baseline", StringComparison.OrdinalIgnoreCase)
                                    || profile.Equals("Main", StringComparison.OrdinalIgnoreCase)
                                    || profile.Equals("High", StringComparison.OrdinalIgnoreCase));
        var pixelFormat = video.PixelFormat?.Trim();
        var compatiblePixelFormat = pixelFormat is not null
                                    && (pixelFormat.Equals("yuv420p", StringComparison.OrdinalIgnoreCase)
                                        || pixelFormat.Equals("yuvj420p", StringComparison.OrdinalIgnoreCase));
        return compatibleProfile && compatiblePixelFormat;
    }
}
