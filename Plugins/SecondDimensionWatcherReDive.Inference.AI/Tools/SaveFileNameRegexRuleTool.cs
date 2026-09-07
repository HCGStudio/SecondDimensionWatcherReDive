using System.ComponentModel;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[Tool<SaveFileNameRegexRuleParams>(
    "save_filename_regex_rule",
    "Validate and save a .NET regex for the current anime, then return every current file it matches with the extracted season and episode. The regex must use a named 'episode' capture group and may use a named 'season' capture group.",
    ToolRiskLevel.Mutating)]
internal sealed partial class SaveFileNameRegexRuleTool(
    IFileNameRegexRuleRepository ruleRepository,
    FileNameInferenceContext context) : ITool
{
    public async Task<IToolResult> ExecuteCoreAsync(
        SaveFileNameRegexRuleParams param,
        CancellationToken cancellationToken)
    {
        var request = context.Current;
        if (request is null || request.Files.Count == 0)
            return new ToolFailureResult("No filename inference batch is active.");

        if (!FileNameRegexMatcher.TryCreateRegex(param.Pattern, out var regex, out var error))
            return new ToolFailureResult(error!);

        var matches = new List<FileNameRegexMatch>();
        var unmatched = new List<string>();
        foreach (var file in request.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var match = FileNameRegexMatcher.Match(regex!, file);

            if (match is null)
            {
                unmatched.Add(file.FilePath);
                continue;
            }

            matches.Add(new FileNameRegexMatch(
                file.FilePath,
                file.FileName,
                match.Season ?? request.DefaultSeason,
                match.Episode));
        }

        if (matches.Count == 0)
            return new ToolFailureResult("The regex did not extract an episode from any current file, so it was not saved.");

        var existingResults = request.ExistingResults?
            .ToDictionary(result => result.FilePath, StringComparer.Ordinal);
        if (existingResults is not null)
        {
            foreach (var match in matches)
            {
                if (!existingResults.TryGetValue(match.FilePath, out var existing)) continue;
                var seasonConflicts = match.Season is not null
                                      && existing.Season is not null
                                      && match.Season != existing.Season;
                if (match.Episode != existing.Episode || seasonConflicts)
                    return new ToolFailureResult(
                        $"The regex conflicts with the existing result for '{match.FilePath}', so it was not saved.");
            }
        }

        var candidate = new FileNameRegexRule(
            Guid.NewGuid(),
            request.AnimationId,
            param.Pattern,
            param.Description,
            DateTimeOffset.UtcNow);
        var rule = await ruleRepository.GetOrAddAsync(candidate, cancellationToken);
        var created = rule.Id == candidate.Id;

        return new ToolSuccessResult<SaveFileNameRegexRuleResult>(new(
            rule.Id,
            created,
            matches,
            unmatched));
    }
}

internal sealed record SaveFileNameRegexRuleParams(
    [property: Description(
        "A .NET-compatible regex. It must contain (?<episode>...) and may contain (?<season>...). Make the pattern specific enough not to match unrelated release formats.")]
    string Pattern,
    [property: Description("A short human-readable description of the release filename format.")]
    string? Description);

internal sealed record SaveFileNameRegexRuleResult(
    Guid RuleId,
    bool Created,
    IReadOnlyList<FileNameRegexMatch> Matches,
    IReadOnlyList<string> UnmatchedFiles);

internal sealed record FileNameRegexMatch(
    string FilePath,
    string FileName,
    int? Season,
    int Episode);
