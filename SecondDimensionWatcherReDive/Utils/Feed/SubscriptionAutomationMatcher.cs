using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;

namespace SecondDimensionWatcherReDive.Utils.Feed;

public sealed class SubscriptionAutomationMatcher(
    ISubscriptionReleaseMetadataExtractor metadataExtractor) : ISubscriptionAutomationMatcher
{
    public SubscriptionAutomationEvaluation Evaluate(
        SubscriptionAutomationPolicy policy,
        AnimationAddRequest release)
    {
        var metadata = metadataExtractor.Extract(release);
        var explanations = new List<SubscriptionAutomationExplanation>(6)
        {
            EvaluateValue(
                "subtitleGroup",
                metadata.SubtitleGroup,
                policy.SubtitleGroups,
                NormalizeSubtitleGroup,
                "字幕组"),
            EvaluateValue(
                "resolution",
                metadata.Resolution,
                policy.Resolutions,
                NormalizeResolution,
                "分辨率"),
            EvaluateValue(
                "codec",
                metadata.Codec,
                policy.Codecs,
                NormalizeCodec,
                "编码"),
            EvaluateLanguages(metadata.Languages, policy.Languages),
            EvaluateSize(metadata.SizeBytes, policy.MinSizeBytes, policy.MaxSizeBytes),
            EvaluateExcludedKeywords(release, policy.ExcludedKeywords)
        };

        return new SubscriptionAutomationEvaluation(
            explanations.All(item => item.Passed),
            metadata,
            explanations);
    }

    private static SubscriptionAutomationExplanation EvaluateValue(
        string field,
        string? actual,
        IReadOnlyList<string> allowed,
        Func<string, string> normalize,
        string displayName)
    {
        var expected = JoinValues(allowed);
        if (allowed.Count == 0)
            return new SubscriptionAutomationExplanation(
                field,
                true,
                actual,
                null,
                $"未限制{displayName}");

        if (string.IsNullOrWhiteSpace(actual))
            return new SubscriptionAutomationExplanation(
                field,
                false,
                null,
                expected,
                $"条目中未识别出{displayName}");

        var normalizedActual = normalize(actual);
        var passed = allowed.Any(value => normalize(value) == normalizedActual);
        return new SubscriptionAutomationExplanation(
            field,
            passed,
            actual,
            expected,
            passed ? $"{displayName}命中允许值" : $"{displayName}不在允许值中");
    }

    private static SubscriptionAutomationExplanation EvaluateLanguages(
        IReadOnlyList<string> actual,
        IReadOnlyList<string> allowed)
    {
        var actualText = JoinValues(actual);
        var expected = JoinValues(allowed);
        if (allowed.Count == 0)
            return new SubscriptionAutomationExplanation(
                "languages",
                true,
                actualText,
                null,
                "未限制语言");

        if (actual.Count == 0)
            return new SubscriptionAutomationExplanation(
                "languages",
                false,
                null,
                expected,
                "条目中未识别出语言");

        var allowedValues = allowed.Select(NormalizeLanguage).ToHashSet(StringComparer.Ordinal);
        var passed = actual.Select(NormalizeLanguage).Any(allowedValues.Contains);
        return new SubscriptionAutomationExplanation(
            "languages",
            passed,
            actualText,
            expected,
            passed ? "至少一种语言命中允许值" : "语言不在允许值中");
    }

    private static SubscriptionAutomationExplanation EvaluateSize(
        long? actual,
        long? minimum,
        long? maximum)
    {
        var expected = FormatSizeRange(minimum, maximum);
        if (minimum is null && maximum is null)
            return new SubscriptionAutomationExplanation(
                "size",
                true,
                actual is null ? null : FormatBytes(actual.Value),
                null,
                "未限制大小");

        if (actual is null)
            return new SubscriptionAutomationExplanation(
                "size",
                false,
                null,
                expected,
                "Feed 条目未提供发布大小");

        var passed = (minimum is null || actual >= minimum) &&
                     (maximum is null || actual <= maximum);
        return new SubscriptionAutomationExplanation(
            "size",
            passed,
            FormatBytes(actual.Value),
            expected,
            passed ? "发布大小在允许范围内" : "发布大小超出允许范围");
    }

    private static SubscriptionAutomationExplanation EvaluateExcludedKeywords(
        AnimationAddRequest release,
        IReadOnlyList<string> excludedKeywords)
    {
        var expected = JoinValues(excludedKeywords);
        if (excludedKeywords.Count == 0)
            return new SubscriptionAutomationExplanation(
                "excludedKeywords",
                true,
                null,
                null,
                "未配置排除词");

        // Exclusion terms intentionally target the release title. Descriptions often contain
        // synopsis text and would otherwise produce surprising false positives.
        var source = release.Title;
        var found = excludedKeywords.FirstOrDefault(keyword =>
            !string.IsNullOrWhiteSpace(keyword) &&
            source.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase));
        var passed = found is null;
        return new SubscriptionAutomationExplanation(
            "excludedKeywords",
            passed,
            found,
            expected,
            passed ? "未命中任何排除词" : $"命中排除词“{found}”");
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeSubtitleGroup(string value)
    {
        return Normalize(value).Trim('[', ']', '【', '】').Trim();
    }

    private static string NormalizeResolution(string value)
    {
        var normalized = Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            "4K" or "UHD" or "2160" or "2160P" => "2160P",
            "1440" or "1440P" => "1440P",
            "1080" or "1080P" or "FHD" => "1080P",
            "720" or "720P" or "HD" => "720P",
            "576" or "576P" => "576P",
            "480" or "480P" => "480P",
            _ => normalized
        };
    }

    private static string NormalizeCodec(string value)
    {
        var normalized = Normalize(value)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            "H265" or "X265" or "HEVC" => "HEVC",
            "H264" or "X264" or "AVC" => "AVC",
            _ => normalized
        };
    }

    private static string NormalizeLanguage(string value)
    {
        var normalized = Normalize(value)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return normalized switch
        {
            "CHS" or "SC" or "GB" or "ZHCN" or "简体" or "简中" or "簡中" or "简体中文" => "ZH-HANS",
            "CHT" or "TC" or "BIG5" or "ZHTW" or "ZHHK" or "繁体" or "繁體" or "繁中" or "繁體中文" => "ZH-HANT",
            "JPN" or "JAP" or "JA" or "日语" or "日語" or "日本語" or "JAPANESE" => "JA",
            "ENG" or "EN" or "英语" or "英語" or "ENGLISH" => "EN",
            _ => normalized
        };
    }

    private static string? JoinValues(IReadOnlyList<string> values)
    {
        var filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return filtered.Length == 0 ? null : string.Join(", ", filtered);
    }

    private static string FormatSizeRange(long? minimum, long? maximum)
    {
        return (minimum, maximum) switch
        {
            ({ } min, { } max) => $"{FormatBytes(min)} – {FormatBytes(max)}",
            ({ } min, null) => $"≥ {FormatBytes(min)}",
            (null, { } max) => $"≤ {FormatBytes(max)}",
            _ => string.Empty
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (decimal)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]} ({bytes} bytes)";
    }
}
