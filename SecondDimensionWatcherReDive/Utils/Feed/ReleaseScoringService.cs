using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;

namespace SecondDimensionWatcherReDive.Utils.Feed;

public sealed class ReleaseScoringService : IReleaseScoringService
{
    public ReleaseScore Score(
        SubscriptionReleaseMetadata metadata,
        SubscriptionAutomationPolicy? policy)
    {
        var reasons = new List<string>();
        var score = ScoreResolution(metadata.Resolution, reasons) +
                    ScoreCodec(metadata.Codec, reasons) +
                    ScoreSubtitleGroup(metadata.SubtitleGroup, policy, reasons) +
                    ScoreLanguages(metadata.Languages, policy, reasons) +
                    ScoreSize(metadata.SizeBytes, reasons);
        return new ReleaseScore(score, reasons);
    }

    private static int ScoreResolution(string? resolution, ICollection<string> reasons)
    {
        var value = resolution?.Trim().ToUpperInvariant() switch
        {
            "2160P" or "4K" or "UHD" => 400,
            "1440P" => 300,
            "1080P" => 200,
            "720P" => 100,
            "576P" => 60,
            "480P" => 40,
            _ => 0
        };
        if (value > 0) reasons.Add($"resolution:{resolution}:+{value}");
        return value;
    }

    private static int ScoreCodec(string? codec, ICollection<string> reasons)
    {
        var value = codec?.Trim().ToUpperInvariant() switch
        {
            "AV1" => 80,
            "HEVC" or "H265" or "H.265" => 60,
            "AVC" or "H264" or "H.264" => 40,
            "VP9" => 30,
            _ => 0
        };
        if (value > 0) reasons.Add($"codec:{codec}:+{value}");
        return value;
    }

    private static int ScoreSubtitleGroup(
        string? group,
        SubscriptionAutomationPolicy? policy,
        ICollection<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(group)) return 0;
        var index = policy?.SubtitleGroups
            .Select((value, position) => (value, position))
            .Where(item => string.Equals(
                item.value.Trim('[', ']', '【', '】'),
                group.Trim('[', ']', '【', '】'),
                StringComparison.OrdinalIgnoreCase))
            .Select(item => (int?)item.position)
            .FirstOrDefault();
        var value = policy is { SubtitleGroups.Count: > 0 }
            ? index is { } position ? Math.Max(10, 50 - position * 5) : 0
            : 20;
        reasons.Add($"subtitleGroup:{group}:+{value}");
        return value;
    }

    private static int ScoreLanguages(
        IReadOnlyList<string> languages,
        SubscriptionAutomationPolicy? policy,
        ICollection<string> reasons)
    {
        var preferred = policy?.Languages ?? [];
        var score = 0;
        foreach (var language in languages.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = preferred.Count == 0 || preferred.Contains(language, StringComparer.OrdinalIgnoreCase)
                ? 20
                : 5;
            score += value;
            reasons.Add($"language:{language}:+{value}");
        }
        return score;
    }

    private static int ScoreSize(long? sizeBytes, ICollection<string> reasons)
    {
        if (sizeBytes is not > 0) return 0;
        var gibibytes = sizeBytes.Value / (1024d * 1024 * 1024);
        var value = gibibytes switch
        {
            >= 8 => 40,
            >= 2 => 25,
            >= 0.7 => 10,
            _ => 5
        };
        reasons.Add($"size:{gibibytes:F2}GiB:+{value}");
        return value;
    }
}
