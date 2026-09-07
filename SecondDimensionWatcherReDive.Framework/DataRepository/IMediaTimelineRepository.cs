namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record MediaTimelinePoint(string Kind, string Name, double StartSeconds, double EndSeconds, bool Enabled);
public sealed record MediaTimelineData(string Key, double DurationSeconds, IReadOnlyList<MediaTimelinePoint> Points, DateTimeOffset UpdatedAt);
public sealed record MediaTimelineContext(string MediaVersion, string? SeasonKey, MediaTimelineData? Episode,
    MediaTimelineData? SeasonDefault, bool SeasonAccepted);

public interface IMediaTimelineRepository
{
    Task<MediaTimelineContext> GetAsync(string mediaVersion, string? seasonKey, CancellationToken cancellationToken);
    Task SaveAsync(string mediaVersion, Guid mappingId, string? seasonKey, bool seasonDefault, double durationSeconds,
        IReadOnlyList<MediaTimelinePoint> points, CancellationToken cancellationToken);
    Task AcceptSeasonAsync(string mediaVersion, Guid mappingId, string seasonKey, double durationSeconds, CancellationToken cancellationToken);
    Task DeleteAsync(string mediaVersion, string? seasonKey, bool seasonDefault, CancellationToken cancellationToken);
}
