using System.Globalization;
using System.Text.RegularExpressions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Utils.MetadataReview;

// Registration keeps the scheduled task available when deterministic rules can run without AI.
public sealed class MetadataRecognitionRuleSupport;

public sealed record MetadataRecognitionRuleDraft(
    string Name, bool Enabled, Guid? SourceFeedId, string? TitlePattern, string? SubtitleGroup,
    string TmdbId, int? FixedSeason, int EpisodeOffset, string? CanonicalGroupName,
    Guid? CreatedFromItemId, long? ExpectedRevision);

public sealed record MetadataRecognitionSample(
    Guid ItemId, string Title, long Revision, string? CurrentTmdbId, int? CurrentSeason,
    int? CurrentEpisode, string? CurrentGroupName, string TmdbId, int? Season, int? Episode,
    string? GroupName, string? Warning);

public sealed record MetadataRecognitionRulePreview(
    int ScannedCount, int MatchCount, bool Truncated, IReadOnlyList<MetadataRecognitionSample> Samples);

public sealed record MetadataRecognitionRuleSeed(
    Guid ItemId, string Title, Guid? SourceFeedId, string? SubtitleGroup, string? TmdbId,
    int? Season, string? GroupName);

public sealed class MetadataRecognitionAmbiguousException(string message) : Exception(message);

public sealed class MetadataRecognitionRuleService(
    IMetadataRecognitionRuleRepository repository,
    IAnimationInfoRepository animationInfoRepository,
    IFeedRepository feedRepository,
    IMetadataReviewService reviewService)
{
    public async Task<MetadataRecognitionRule> ValidateAsync(MetadataRecognitionRuleDraft draft,
        Guid? id, CancellationToken cancellationToken)
    {
        var name = draft.Name?.Trim();
        var titlePattern = Normalize(draft.TitlePattern);
        var subtitleGroup = Normalize(draft.SubtitleGroup);
        var canonicalGroup = Normalize(draft.CanonicalGroupName);
        if (string.IsNullOrEmpty(name) || name.Length > 200)
            throw Invalid("ruleName", "Rule name is required and cannot exceed 200 characters.");
        if (draft.SourceFeedId is null && titlePattern is null && subtitleGroup is null)
            throw Invalid("ruleScope", "Choose a subscription source, title pattern, or subtitle group.");
        if (titlePattern?.Length > 1000 || subtitleGroup?.Length > 200 || canonicalGroup?.Length > 200)
            throw Invalid("ruleLength", "A pattern cannot exceed 1000 characters; group names cannot exceed 200.");
        if (titlePattern is not null)
        {
            try
            {
                var regex = CreateRegex(titlePattern);
                if (regex.IsMatch("")) throw Invalid("rulePattern", "A title pattern must not match empty text.");
            }
            catch (ArgumentException) { throw Invalid("rulePattern", "The title pattern is not a valid regular expression."); }
            catch (RegexMatchTimeoutException) { throw Invalid("rulePattern", "The title pattern took too long to evaluate."); }
        }
        if (!int.TryParse(draft.TmdbId, out var tmdbId) || tmdbId <= 0)
            throw Invalid("invalidTmdbId", "TMDB ID must be a positive integer.");
        if (draft.FixedSeason < 0 || draft.EpisodeOffset is < -10000 or > 10000)
            throw Invalid("ruleNumbers", "Season cannot be negative; episode offset must be between -10000 and 10000.");
        if (draft.SourceFeedId is { } sourceId
            && await feedRepository.FindByIdAsync(sourceId, cancellationToken) is null)
            throw Invalid("ruleSource", "The subscription source no longer exists.");
        MetadataRecognitionRule? current = null;
        if (id is { } ruleId)
        {
            current = await repository.FindAsync(ruleId, cancellationToken)
                      ?? throw new MetadataReviewNotFoundException("ruleNotFound", "The rule was not found.");
            if (draft.ExpectedRevision != current.Revision)
                throw new MetadataReviewConflictException("ruleChanged", "The rule changed. Refresh it before saving.");
        }
        else if (draft.CreatedFromItemId is { } itemId)
        {
            var item = await animationInfoRepository.FindByIdAsync(itemId, cancellationToken);
            if (item?.MetadataStatus != MetadataReviewStatus.Reviewed)
                throw Invalid("ruleCorrection", "Complete the individual correction before creating its rule.");
        }
        var now = DateTimeOffset.UtcNow;
        return new MetadataRecognitionRule(id ?? Guid.NewGuid(), name, draft.Enabled,
            (current?.Revision ?? 0) + 1, draft.SourceFeedId, titlePattern, subtitleGroup,
            tmdbId.ToString(CultureInfo.InvariantCulture), draft.FixedSeason, draft.EpisodeOffset,
            canonicalGroup, current?.CreatedFromItemId ?? draft.CreatedFromItemId,
            current?.CreatedAt ?? now, now);
    }

    public async Task<MetadataRecognitionRule> SaveAsync(MetadataRecognitionRuleDraft draft,
        Guid? id, CancellationToken cancellationToken)
    {
        var rule = await ValidateAsync(draft, id, cancellationToken);
        if (id is null && (await repository.ListAsync(cancellationToken)).Count >= 200)
            throw Invalid("ruleLimit", "Up to 200 recognition rules are supported.");
        if (!await repository.SaveAsync(rule, id is null ? null : draft.ExpectedRevision, cancellationToken))
            throw new MetadataReviewConflictException("ruleChanged", "The rule changed. Refresh it before saving.");
        return rule;
    }

    public async Task<MetadataRecognitionRuleSeed> GetSeedAsync(Guid itemId, CancellationToken cancellationToken)
    {
        var item = await animationInfoRepository.FindByIdAsync(itemId, cancellationToken)
                   ?? throw new MetadataReviewNotFoundException("itemNotFound", "The item was not found.");
        if (item.MetadataStatus != MetadataReviewStatus.Reviewed)
            throw Invalid("ruleCorrection", "Complete the individual correction before creating its rule.");
        return new MetadataRecognitionRuleSeed(item.Id, item.Title, item.SourceFeedId,
            item.ReleaseSubtitleGroup, item.Animation?.TmdbId, item.Season, item.Group?.Name);
    }

    public async Task<MetadataRecognitionRulePreview> PreviewAsync(MetadataRecognitionRuleDraft draft,
        Guid? id, CancellationToken cancellationToken)
    {
        var rule = await ValidateAsync(draft, id, cancellationToken);
        var candidates = await repository.GetCandidatesAsync(rule.SourceFeedId, 1001, cancellationToken);
        var others = (await repository.ListAsync(cancellationToken))
            .Where(other => other.Enabled && other.Id != id).ToList();
        var samples = new List<MetadataRecognitionSample>();
        var matchCount = 0;
        foreach (var item in candidates.Take(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Matches(rule, item)) continue;
            matchCount++;
            if (samples.Count >= 50) continue;
            string? warning = null;
            InferenceResult? result = null;
            try
            {
                if (others.Any(other => Matches(other, item)))
                    warning = "conflict";
                result = Apply(rule, item, ExistingMetadata(item));
                if (result.Season is null) warning ??= "needsInference";
            }
            catch (MetadataRecognitionAmbiguousException) { warning = "ambiguous"; }
            samples.Add(new MetadataRecognitionSample(item.Id, item.Title, item.StateVersion,
                item.Animation?.TmdbId, item.Season, item.Episode, item.Group?.Name, rule.TmdbId,
                result?.Season, result?.Episode, result?.GroupName, warning));
        }
        return new MetadataRecognitionRulePreview(Math.Min(candidates.Count, 1000), matchCount,
            candidates.Count > 1000, samples);
    }

    public async Task<MetadataReviewPreviewResult> PreviewHistoryAsync(Guid ruleId, Guid itemId,
        long ruleRevision, long itemRevision, CancellationToken cancellationToken)
    {
        var rules = await repository.ListAsync(cancellationToken);
        var rule = rules.SingleOrDefault(candidate => candidate.Id == ruleId)
                   ?? throw new MetadataReviewNotFoundException("ruleNotFound", "The rule was not found.");
        if (rule.Revision != ruleRevision)
            throw new MetadataReviewConflictException("ruleChanged", "The rule changed. Refresh its preview.");
        var item = await animationInfoRepository.FindByIdAsync(itemId, cancellationToken)
                   ?? throw new MetadataReviewNotFoundException("itemNotFound", "The item was not found.");
        if (!Matches(rule, item) || rules.Any(other => other.Enabled && other.Id != ruleId && Matches(other, item)))
            throw new MetadataReviewConflictException("ruleConflict", "The rule no longer matches unambiguously.");
        var result = Apply(rule, item, ExistingMetadata(item));
        return await reviewService.PreviewAsync(itemId, itemRevision,
            new MetadataReviewCorrection(result.TmdbId, result.Season, result.Episode, result.GroupName),
            cancellationToken);
    }

    public static MetadataRecognitionRule? Select(IReadOnlyList<MetadataRecognitionRule> rules, AnimationInfo item)
    {
        var matches = rules.Where(rule => rule.Enabled
                                         && item.IngestedAt >= rule.EffectiveFrom
                                         && Matches(rule, item)).ToList();
        if (matches.Count > 1)
            throw new MetadataRecognitionAmbiguousException(
                $"Recognition rules conflict: {string.Join(", ", matches.Select(rule => rule.Name))}. Review this release manually.");
        return matches.SingleOrDefault();
    }

    public static bool Matches(MetadataRecognitionRule rule, AnimationInfo item)
    {
        if (rule.SourceFeedId is not null && rule.SourceFeedId != item.SourceFeedId) return false;
        if (rule.SubtitleGroup is not null && !string.Equals(rule.SubtitleGroup,
                item.ReleaseSubtitleGroup ?? item.Group?.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (rule.TitlePattern is null) return true;
        try { return CreateRegex(rule.TitlePattern).IsMatch(item.Title); }
        catch (RegexMatchTimeoutException)
        {
            throw new MetadataRecognitionAmbiguousException($"Title matching timed out for rule '{rule.Name}'. Review this release manually.");
        }
    }

    public static InferenceResult Apply(MetadataRecognitionRule rule, AnimationInfo item, InferenceResult? fallback)
    {
        int? season = null, episode = null;
        if (rule.TitlePattern is not null)
        {
            try
            {
                var matches = CreateRegex(rule.TitlePattern).Matches(item.Title);
                if (matches.Count != 1)
                    throw new MetadataRecognitionAmbiguousException($"Rule '{rule.Name}' does not match a single title segment.");
                season = ReadNumber(matches[0], "season");
                episode = ReadNumber(matches[0], "episode");
            }
            catch (RegexMatchTimeoutException)
            {
                throw new MetadataRecognitionAmbiguousException($"Title matching timed out for rule '{rule.Name}'.");
            }
        }
        season = rule.FixedSeason ?? season ?? fallback?.Season;
        episode ??= fallback?.Episode;
        if (episode is null && rule.EpisodeOffset != 0)
            throw new MetadataRecognitionAmbiguousException($"Rule '{rule.Name}' requires an unambiguous episode before its offset can be applied.");
        if (episode is { } number)
        {
            var shifted = (long)number + rule.EpisodeOffset;
            if (shifted < 0 || shifted > int.MaxValue)
                throw new MetadataRecognitionAmbiguousException($"Rule '{rule.Name}' produces an invalid episode number.");
            episode = (int)shifted;
        }
        return new InferenceResult(rule.TmdbId,
            rule.CanonicalGroupName ?? fallback?.GroupName ?? item.ReleaseSubtitleGroup,
            season, episode, fallback?.Confidence ?? 1);
    }

    public static bool CanResolveWithoutAi(MetadataRecognitionRule rule, AnimationInfo item)
    {
        if (rule.TitlePattern is null) return false;
        // An explicit episode capture avoids guessing from unrelated title numbers or a batch range.
        try
        {
            var match = CreateRegex(rule.TitlePattern).Match(item.Title);
            return match.Groups["episode"].Success
                   && (rule.FixedSeason is not null || match.Groups["season"].Success);
        }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static InferenceResult ExistingMetadata(AnimationInfo item) =>
        new(item.Animation?.TmdbId, item.Group?.Name, item.Season, item.Episode, item.MetadataConfidence);

    private static int? ReadNumber(Match match, string group)
    {
        var capture = match.Groups[group];
        if (!capture.Success) return null;
        if (capture.Captures.Count != 1 || !int.TryParse(capture.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var number))
            throw new MetadataRecognitionAmbiguousException($"The {group} capture is ambiguous or is not a non-negative integer.");
        return number;
    }

    private static Regex CreateRegex(string pattern) => new(pattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static MetadataReviewValidationException Invalid(string code, string message) => new(code, message);
}
