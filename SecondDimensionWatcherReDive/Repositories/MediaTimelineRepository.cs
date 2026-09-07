using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

[JsonSerializable(typeof(MediaTimelinePoint[]))]
internal partial class TimelineJsonContext : JsonSerializerContext;

internal sealed class MediaTimelineRepository(Models.ApplicationContext context) : IMediaTimelineRepository
{
    public async Task<MediaTimelineContext> GetAsync(string mediaVersion, string? seasonKey, CancellationToken cancellationToken)
    {
        var episode = await context.Set<Models.MediaTimeline>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == "media:" + mediaVersion, cancellationToken);
        var defaults = seasonKey is null ? null : await context.Set<Models.MediaTimeline>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == seasonKey, cancellationToken);
        var accepted = defaults is not null && await context.Set<Models.MediaTimelineBinding>().AnyAsync(x =>
            x.MediaVersion == mediaVersion && x.SeasonKey == seasonKey && x.AcceptedRevision == defaults.UpdatedAt, cancellationToken);
        return new MediaTimelineContext(mediaVersion, seasonKey, ToData(episode), ToData(defaults), accepted);
    }

    public async Task SaveAsync(string mediaVersion, Guid mappingId, string? seasonKey, bool seasonDefault, double durationSeconds,
        IReadOnlyList<MediaTimelinePoint> points, CancellationToken cancellationToken)
    {
        var key = seasonDefault ? seasonKey ?? throw new ArgumentException("Season unavailable") : "media:" + mediaVersion;
        var row = await context.Set<Models.MediaTimeline>().FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (row is null) { row = new Models.MediaTimeline { Key = key }; context.Add(row); }
        row.DurationSeconds = durationSeconds;
        row.PointsJson = JsonSerializer.Serialize(points.ToArray(), TimelineJsonContext.Default.MediaTimelinePointArray);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (seasonDefault)
        {
            // New season revisions require confirmation for other media versions.
            var binding = await context.Set<Models.MediaTimelineBinding>().FindAsync([mediaVersion], cancellationToken);
            if (binding is null) { binding = new Models.MediaTimelineBinding { MediaVersion = mediaVersion }; context.Add(binding); }
            binding.MappingId = mappingId; binding.SeasonKey = key; binding.DurationSeconds = durationSeconds; binding.AcceptedRevision = row.UpdatedAt;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task AcceptSeasonAsync(string mediaVersion, Guid mappingId, string seasonKey, double durationSeconds, CancellationToken cancellationToken)
    {
        var row = await context.Set<Models.MediaTimeline>().FirstOrDefaultAsync(x => x.Key == seasonKey, cancellationToken)
                  ?? throw new KeyNotFoundException();
        var data = ToData(row)!;
        if (data.Points.Any(x => x.EndSeconds > durationSeconds || x.StartSeconds >= durationSeconds))
            throw new ArgumentException("Season points exceed this media duration");
        var binding = await context.Set<Models.MediaTimelineBinding>().FindAsync([mediaVersion], cancellationToken);
        if (binding is null) { binding = new Models.MediaTimelineBinding { MediaVersion = mediaVersion }; context.Add(binding); }
        binding.MappingId = mappingId; binding.SeasonKey = seasonKey; binding.DurationSeconds = durationSeconds; binding.AcceptedRevision = row.UpdatedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string mediaVersion, string? seasonKey, bool seasonDefault, CancellationToken cancellationToken)
    {
        var key = seasonDefault ? seasonKey : "media:" + mediaVersion;
        await context.Set<Models.MediaTimeline>().Where(x => x.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    private static MediaTimelineData? ToData(Models.MediaTimeline? row) => row is null ? null : new MediaTimelineData(row.Key,
        row.DurationSeconds, JsonSerializer.Deserialize(row.PointsJson, TimelineJsonContext.Default.MediaTimelinePointArray) ?? [], row.UpdatedAt);
}
