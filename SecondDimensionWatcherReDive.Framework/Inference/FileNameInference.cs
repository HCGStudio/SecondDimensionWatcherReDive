using System.Globalization;
using System.Text.RegularExpressions;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Framework.Inference;

/// <summary>
///     A file offered to the filename inference pipeline. FilePath is relative to the download root
///     and uniquely identifies files that may share the same basename.
/// </summary>
public sealed record FileNameInferenceInput(string FilePath, string FileName);

public sealed record FileNameInferenceResult(string FilePath, int? Season, int Episode);

public sealed record FileNameInferenceRequest(
    Guid AnimationId,
    string Context,
    IReadOnlyList<FileNameInferenceInput> Files,
    bool AllowRegexRuleCreation,
    IReadOnlyList<string>? TargetFilePaths = null,
    IReadOnlyList<FileNameInferenceResult>? ExistingResults = null,
    int? DefaultSeason = null);

public static class FileNameRegexMatcher
{
    public const int MaxRulesPerAnimation = 100;
    // Keep indexed patterns below PostgreSQL's B-tree entry-size limit even for
    // four-byte UTF-8 characters.
    public const int MaxPatternLength = 512;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static bool TryCreateRegex(string pattern, out Regex? regex, out string? error)
    {
        regex = null;
        error = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "The regex pattern cannot be empty.";
            return false;
        }

        if (pattern.Length > MaxPatternLength)
        {
            error = $"The regex pattern cannot exceed {MaxPatternLength} characters.";
            return false;
        }

        try
        {
            regex = new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                MatchTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            error = $"Invalid regex pattern: {ex.Message}";
            return false;
        }

        if (!regex.GetGroupNames().Contains("episode", StringComparer.Ordinal))
        {
            regex = null;
            error = "The regex pattern must contain an 'episode' named capture group.";
            return false;
        }

        return true;
    }

    public static FileNameInferenceResult? Match(
        Regex regex,
        FileNameInferenceInput file)
    {
        Match match;
        try
        {
            match = regex.Match(file.FileName);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        if (!match.Success || !TryParseCapture(match.Groups["episode"], out var episode))
            return null;

        int? season = null;
        var seasonGroup = match.Groups["season"];
        if (seasonGroup.Success)
        {
            if (!TryParseCapture(seasonGroup, out var parsedSeason)) return null;
            season = parsedSeason;
        }

        return new FileNameInferenceResult(file.FilePath, season, episode);
    }

    public static FileNameInferenceResult? Match(
        FileNameRegexRule rule,
        FileNameInferenceInput file)
    {
        return TryCreateRegex(rule.Pattern, out var regex, out _)
            ? Match(regex!, file)
            : null;
    }

    private static bool TryParseCapture(Group group, out int value)
    {
        value = 0;
        return group.Success
               && int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value)
               && value >= 0;
    }
}
