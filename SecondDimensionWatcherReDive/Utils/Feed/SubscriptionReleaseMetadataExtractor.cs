using System.Globalization;
using System.Text.RegularExpressions;
using SecondDimensionWatcherReDive.Framework.Feed;

namespace SecondDimensionWatcherReDive.Utils.Feed;

public sealed partial class SubscriptionReleaseMetadataExtractor : ISubscriptionReleaseMetadataExtractor
{
    private static readonly (Regex Pattern, string Value)[] LanguagePatterns =
    [
        (SimplifiedChineseRegex(), "简体中文"),
        (TraditionalChineseRegex(), "繁體中文"),
        (JapaneseRegex(), "日语"),
        (EnglishRegex(), "英语")
    ];

    public SubscriptionReleaseMetadata Extract(AnimationAddRequest release)
    {
        var source = string.IsNullOrWhiteSpace(release.AdditionalDownloadInfo)
            ? release.Title
            : $"{release.Title} {release.AdditionalDownloadInfo}";

        var subtitleGroup = ExtractSubtitleGroup(release.Title);
        var resolution = ExtractResolution(source);
        var codec = ExtractCodec(source);
        var languages = LanguagePatterns
            .Where(item => item.Pattern.IsMatch(source))
            .Select(item => item.Value)
            .ToArray();
        var sizeBytes = release.ContentLength ?? ExtractSize(release.AdditionalDownloadInfo);

        return new SubscriptionReleaseMetadata(
            subtitleGroup,
            resolution,
            codec,
            languages,
            sizeBytes);
    }

    private static string? ExtractSubtitleGroup(string title)
    {
        foreach (Match match in LeadingTagRegex().Matches(title))
        {
            var value = match.Groups["group"].Value.Trim();
            if (!LooksLikeTechnicalTag(value))
                return value;
        }

        return null;
    }

    private static bool LooksLikeTechnicalTag(string value)
    {
        return ResolutionRegex().IsMatch(value) || CodecRegex().IsMatch(value) ||
               LanguagePatterns.Any(item => item.Pattern.IsMatch(value));
    }

    private static string? ExtractResolution(string source)
    {
        var match = ResolutionRegex().Match(source);
        if (!match.Success)
            return null;

        if (match.Groups["fourK"].Success)
            return "2160p";

        return $"{match.Groups["height"].Value}p";
    }

    private static string? ExtractCodec(string source)
    {
        var match = CodecRegex().Match(source);
        if (!match.Success)
            return null;

        var codec = match.Value.Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return codec switch
        {
            "H265" or "X265" or "HEVC" => "HEVC",
            "H264" or "X264" or "AVC" => "AVC",
            "AV1" => "AV1",
            "VP9" => "VP9",
            _ => null
        };
    }

    private static long? ExtractSize(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        var byteMatch = SizeBytesRegex().Match(source);
        if (byteMatch.Success && long.TryParse(
                byteMatch.Groups["bytes"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bytes))
            return bytes;

        var humanMatch = HumanSizeRegex().Match(source);
        if (!humanMatch.Success || !decimal.TryParse(
                humanMatch.Groups["amount"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount))
            return null;

        var multiplier = humanMatch.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "KB" or "KIB" => 1024m,
            "MB" or "MIB" => 1024m * 1024m,
            "GB" or "GIB" => 1024m * 1024m * 1024m,
            "TB" or "TIB" => 1024m * 1024m * 1024m * 1024m,
            _ => 1m
        };

        var result = decimal.Truncate(amount * multiplier);
        return result is >= 0 and <= long.MaxValue ? (long)result : null;
    }

    [GeneratedRegex(@"(?:\G|^)\s*[\[【](?<group>[^\]】]+)[\]】]", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingTagRegex();

    [GeneratedRegex(@"(?<!\d)(?:(?:3840|4096)[x×](?<height>2160)|2560[x×](?<height>1440)|1920[x×](?<height>1080)|1280[x×](?<height>720)|(?<height>2160|1440|1080|720|576|480)[pP])(?!\d)|(?<fourK>\b(?:4K|UHD)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:AV1|HEVC|H[.\-]?265|X265|AVC|H[.\-]?264|X264|VP9)(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodecRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:CHS|SC|GB|ZH[._-]?CN)(?![A-Za-z0-9])|简(?:体|中)|簡中|[简簡]繁", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimplifiedChineseRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:CHT|TC|BIG5|ZH[._-]?(?:TW|HK))(?![A-Za-z0-9])|繁(?:體|体|中)|[简簡]繁", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TraditionalChineseRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:JPN|JAP|JA)(?![A-Za-z0-9])|日(?:语|語)|日本語|Japanese", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:ENG|EN)(?![A-Za-z0-9])|英(?:语|語)|English", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnglishRegex();

    [GeneratedRegex(@"(?:contentLength|sizeBytes|size)\s*[:=]\s*(?<bytes>\d+)(?![.\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeBytesRegex();

    [GeneratedRegex(@"(?<amount>\d+(?:\.\d+)?)\s*(?<unit>KiB|MiB|GiB|TiB|KB|MB|GB|TB)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HumanSizeRegex();
}
