namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record EpisodeAirDate(int Episode, string? AirDate);
public sealed record EpisodeAirCalendar(IReadOnlyList<EpisodeAirDate> Episodes, DateTimeOffset CheckedAt, string Source);
public sealed record CompletionCandidate(Guid ReleaseId, string Title, DateTimeOffset PublishedAt,
    long? SizeBytes, int Score, IReadOnlyList<string> Reasons, bool Eligible, string? UnavailableReason);
public sealed record EpisodeCompletionItem(int Episode, string State, string? AirDate,
    Guid? SelectedReleaseId, IReadOnlyList<CompletionCandidate> Candidates, string Reason);
public sealed record EpisodeCompletionPlan(string TmdbId, string AnimationName, int Season,
    DateTimeOffset GeneratedAt, DateTimeOffset AirDatesCheckedAt, string AirDatesSource,
    int UnidentifiedReleaseCount, IReadOnlyList<EpisodeCompletionItem> Episodes);
public sealed record CompletionSelection(int Episode, Guid ReleaseId);
public sealed record CompletionSubmissionRequest(string TmdbId, int Season, IReadOnlyList<CompletionSelection> Selections);
public sealed record CompletionSubmissionResult(int Episode, Guid ReleaseId, string Outcome, bool IsSuccess);
public interface ILibraryCompletionRepository
{
    Task<IReadOnlyList<AnimationInfo>> GetSeasonReleasesAsync(string tmdbId, int season, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> GetMappedReleaseIdsAsync(string tmdbId, int season, CancellationToken cancellationToken);
    Task<bool> TryClaimEpisodeAsync(string tmdbId, int season, int episode, Guid releaseId, Guid claimId, CancellationToken cancellationToken);
    Task ReleaseClaimAsync(string tmdbId, int season, int episode, Guid claimId, CancellationToken cancellationToken);
}
