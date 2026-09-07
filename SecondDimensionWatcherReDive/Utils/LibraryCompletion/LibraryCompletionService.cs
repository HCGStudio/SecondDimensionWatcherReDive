using System.Text.RegularExpressions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
namespace SecondDimensionWatcherReDive.Utils.LibraryCompletion;

public sealed partial class LibraryCompletionService(ILibraryCompletionRepository repository,
    ISubscriptionAutomationPolicyRepository policies, IMultiSourceSubscriptionRepository sources,
    ISubscriptionAutomationMatcher matcher, IReleaseScoringService scoring,
    EpisodeAirCalendarService calendar, EpisodeDownloadService downloads)
{
    public static bool IsReliable(AnimationInfo info) => info.Season is > 0 && info.Episode is > 0 &&
        info.MetadataStatus is MetadataReviewStatus.Identified or MetadataReviewStatus.Reviewed &&
        !BatchTitle().IsMatch(info.Title);

    [GeneratedRegex(@"(?i)(?:\b(?:batch|complete|全集)\b|合集|全\s*\d+\s*[集話话]|(?:\[|\s)\d{1,3}\s*[-~～]\s*\d{1,3}(?:\]|\s))")]
    private static partial Regex BatchTitle();

    public async Task<EpisodeCompletionPlan> GetPlanAsync(string tmdbId, int season, CancellationToken cancellationToken)
    {
        var releases = await repository.GetSeasonReleasesAsync(tmdbId, season, cancellationToken);
        var mapped = await repository.GetMappedReleaseIdsAsync(tmdbId, season, cancellationToken);
        var air = await calendar.GetAsync(tmdbId, season, cancellationToken);
        var episodeNumbers = air.Episodes.Select(x => x.Episode)
            .Concat(releases.Where(x => x.Episode is > 0).Select(x => x.Episode!.Value));
        var expected = releases.Select(x => x.ExpectedEpisodeCount ?? 0).DefaultIfEmpty().Max();
        episodeNumbers = episodeNumbers.Concat(Enumerable.Range(1, Math.Clamp(expected, 0, 10000)));
        var policyByFeed = (await policies.GetAllOrderedAsync(cancellationToken)).ToDictionary(x => x.FeedId);
        var group = (await sources.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.TmdbId == tmdbId && x.Season == season);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = new List<EpisodeCompletionItem>();
        foreach (var episode in episodeNumbers.Distinct().Order())
        {
            var date = air.Episodes.FirstOrDefault(x => x.Episode == episode)?.AirDate;
            var unaired = DateOnly.TryParse(date, out var parsed) && parsed > today;
            var episodeReleases = releases.Where(x => x.Episode == episode && IsReliable(x)).ToList();
            var candidates = episodeReleases.Select(info =>
            {
                var policy = info.SourceFeedId is { } feedId ? policyByFeed.GetValueOrDefault(feedId) : null;
                if (group != null && info.SourceFeedId is { } linkedId && group.FeedIds.Contains(linkedId)) policy = group.ToPolicy(linkedId);
                return Candidate(info, policy);
            }).OrderByDescending(x => x.Eligible).ThenByDescending(x => x.Score).ThenByDescending(x => x.PublishedAt).ThenBy(x => x.ReleaseId).ToList();
            var downloaded = episodeReleases.Any(x => x.IsDownloadFinished && mapped.Contains(x.Id));
            var mappingPending = episodeReleases.Any(x => x.IsDownloadFinished && !mapped.Contains(x.Id));
            var downloading = episodeReleases.Any(x => x.IsDownloadTracked || x.DownloadCancellationId != null);
            var failed = episodeReleases.Any(x => x.AutomationDisposition == SubscriptionAutomationDisposition.AutoDownloadFailed);
            var selected = unaired || downloaded || downloading ? null : candidates.FirstOrDefault(x => x.Eligible)?.ReleaseId;
            var state = downloaded ? "downloaded" : mappingPending ? "mapping_pending" : downloading ? "downloading" : unaired ? "unaired" : failed ? "failed" :
                candidates.Count > 0 ? "candidate" : date == null ? "air_date_unknown" : "aired_no_resource";
            items.Add(new(episode, state, date, selected, candidates,
                selected != null ? "highest_eligible_score" : candidates.Count > 0 && !downloaded && !downloading && !unaired ? "no_eligible_candidate" : state));
        }
        return new(tmdbId, releases.FirstOrDefault()?.Animation?.Name ?? group?.Name ?? tmdbId, season,
            DateTimeOffset.UtcNow, air.CheckedAt, air.Source, releases.Count(x => !IsReliable(x)), items);
    }

    public CompletionCandidate Candidate(AnimationInfo info, SubscriptionAutomationPolicy? policy)
    {
        var evaluation = policy == null ? null : matcher.Evaluate(policy, new AnimationAddRequest(info.PublishTime,
            info.Title, info.Description, info.DownloadUrl, info.DownloadType, info.AdditionalDownloadInfo,
            info.SourceFeedId, info.ReleaseSizeBytes));
        var score = scoring.Score(new(info.ReleaseSubtitleGroup ?? info.Group?.Name, info.ReleaseResolution,
            info.ReleaseCodec, info.ReleaseLanguages ?? [], info.ReleaseSizeBytes), policy);
        var reason = !IsReliable(info) ? "unidentified_or_batch" : info.DownloadType != FileDownloadTypes.TorrentDownload ? "unsupported_source" :
            info.IsDownloadFinished ? "downloaded" : info.IsDownloadTracked || info.DownloadCancellationId != null ? "downloading" :
            evaluation is { Matched: false } ? "policy_mismatch" : null;
        return new(info.Id, info.Title, info.PublishTime, info.ReleaseSizeBytes, score.Value,
            score.Reasons.Concat(evaluation?.Explanations.Where(x => !x.Passed).Select(x => x.Message) ?? []).ToList(), reason == null, reason);
    }

    public async Task<IReadOnlyList<CompletionSubmissionResult>> SubmitAsync(CompletionSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<CompletionSubmissionResult>();
        foreach (var selection in request.Selections)
        {
            try
            {
                // Rebuild each episode immediately before submission; earlier batch items may have changed library state.
                var plan = await GetPlanAsync(request.TmdbId, request.Season, cancellationToken);
                var episode = plan.Episodes.FirstOrDefault(x => x.Episode == selection.Episode);
                if (episode?.State is "downloaded" or "downloading" or "mapping_pending")
                {
                    results.Add(new(selection.Episode, selection.ReleaseId, "already_present_or_busy", true));
                    continue;
                }
                if (episode == null || episode.State == "unaired" || !episode.Candidates.Any(x => x.ReleaseId == selection.ReleaseId && x.Eligible))
                {
                    results.Add(new(selection.Episode, selection.ReleaseId, "candidate_unavailable", false));
                    continue;
                }
                var info = (await repository.GetSeasonReleasesAsync(request.TmdbId, request.Season, cancellationToken))
                    .FirstOrDefault(x => x.Id == selection.ReleaseId && x.Episode == selection.Episode);
                results.Add(info == null ? new(selection.Episode, selection.ReleaseId, "candidate_unavailable", false) :
                    await downloads.SubmitAsync(info, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                results.Add(new(selection.Episode, selection.ReleaseId, "submission_failed", false));
            }
        }
        return results;
    }
}
