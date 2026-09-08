namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record MediaTimelinePoint(string Kind, string Name, double StartSeconds, double EndSeconds, bool Enabled);
public sealed record MediaTimelineData(string Key, double DurationSeconds, IReadOnlyList<MediaTimelinePoint> Points, DateTimeOffset UpdatedAt);
public sealed record MediaTimelineContext(string MediaVersion, string? SeasonKey, MediaTimelineData? Episode,
    MediaTimelineData? SeasonDefault, bool SeasonAccepted);

public enum MediaTimelineMutationOutcome
{
    NotFound,
    Conflict,
    Success
}

public interface IMediaTimelineRepository
{
    Task<MediaTimelineContext?> GetAsync(string mediaVersion, FileMapping expectedMapping, string? seasonKey, CancellationToken cancellationToken);
    Task<MediaTimelineMutationOutcome> SaveAsync(string mediaVersion, FileMapping expectedMapping, string? seasonKey, bool seasonDefault, double durationSeconds,
        IReadOnlyList<MediaTimelinePoint> points, CancellationToken cancellationToken);
    Task<MediaTimelineMutationOutcome> AcceptSeasonAsync(string mediaVersion, FileMapping expectedMapping, string seasonKey, double durationSeconds, CancellationToken cancellationToken);
    Task<MediaTimelineMutationOutcome> DeleteAsync(string mediaVersion, FileMapping expectedMapping, string? seasonKey, bool seasonDefault, CancellationToken cancellationToken);
}
