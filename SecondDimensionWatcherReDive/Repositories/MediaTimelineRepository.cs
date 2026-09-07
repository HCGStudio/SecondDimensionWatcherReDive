using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

[JsonSerializable(typeof(MediaTimelinePoint[]))]
internal partial class TimelineJsonContext : JsonSerializerContext;

internal sealed class MediaTimelineRepository(Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> options) : IMediaTimelineRepository
{
    public async Task<MediaTimelineContext> GetAsync(string mediaVersion, string? seasonKey, CancellationToken cancellationToken)
    {
        var episode = await context.Set<Models.MediaTimeline>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == "media:" + mediaVersion, cancellationToken);
        var defaults = seasonKey is null ? null : await context.Set<Models.MediaTimeline>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == seasonKey, cancellationToken);
        var accepted = defaults is not null && await context.Set<Models.MediaTimelineBinding>().AnyAsync(x =>
            x.MediaVersion == mediaVersion && x.SeasonKey == seasonKey && x.AcceptedRevision == defaults.UpdatedAt, cancellationToken);
        return new MediaTimelineContext(mediaVersion, seasonKey, ToData(episode), ToData(defaults), accepted);
    }

    public Task<MediaTimelineMutationOutcome> SaveAsync(string mediaVersion, FileMapping expectedMapping, string? seasonKey, bool seasonDefault, double durationSeconds,
        IReadOnlyList<MediaTimelinePoint> points, CancellationToken cancellationToken) =>
        WriteAsync(expectedMapping, seasonKey, async write =>
        {
            var key = seasonDefault ? seasonKey ?? throw new ArgumentException("Season unavailable") : "media:" + mediaVersion;
            var row = await write.Set<Models.MediaTimeline>().FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
            if (row is null) { row = new Models.MediaTimeline { Key = key }; write.Add(row); }
            row.DurationSeconds = durationSeconds;
            row.PointsJson = JsonSerializer.Serialize(points.ToArray(), TimelineJsonContext.Default.MediaTimelinePointArray);
            row.UpdatedAt = DateTimeOffset.UtcNow;
            if (seasonDefault)
            {
                // New season revisions require confirmation for other media versions.
                var binding = await write.Set<Models.MediaTimelineBinding>().FindAsync([mediaVersion], cancellationToken);
                if (binding is null) { binding = new Models.MediaTimelineBinding { MediaVersion = mediaVersion }; write.Add(binding); }
                binding.MappingId = expectedMapping.Id; binding.SeasonKey = key; binding.DurationSeconds = durationSeconds; binding.AcceptedRevision = row.UpdatedAt;
            }
        }, cancellationToken);

    public Task<MediaTimelineMutationOutcome> AcceptSeasonAsync(string mediaVersion, FileMapping expectedMapping, string seasonKey, double durationSeconds, CancellationToken cancellationToken) =>
        WriteAsync(expectedMapping, seasonKey, async write =>
        {
            var row = await write.Set<Models.MediaTimeline>().FirstOrDefaultAsync(x => x.Key == seasonKey, cancellationToken)
                      ?? throw new KeyNotFoundException();
            var data = ToData(row)!;
            if (data.Points.Any(x => x.Enabled && (x.EndSeconds > durationSeconds || x.StartSeconds >= durationSeconds)))
                throw new ArgumentException("Season points exceed this media duration");
            var binding = await write.Set<Models.MediaTimelineBinding>().FindAsync([mediaVersion], cancellationToken);
            if (binding is null) { binding = new Models.MediaTimelineBinding { MediaVersion = mediaVersion }; write.Add(binding); }
            binding.MappingId = expectedMapping.Id; binding.SeasonKey = seasonKey; binding.DurationSeconds = durationSeconds; binding.AcceptedRevision = row.UpdatedAt;
        }, cancellationToken);

    public Task<MediaTimelineMutationOutcome> DeleteAsync(string mediaVersion, FileMapping expectedMapping, string? seasonKey, bool seasonDefault, CancellationToken cancellationToken) =>
        WriteAsync(expectedMapping, seasonKey, async write =>
        {
            var key = seasonDefault ? seasonKey : "media:" + mediaVersion;
            await write.Set<Models.MediaTimeline>().Where(x => x.Key == key).ExecuteDeleteAsync(cancellationToken);
        }, cancellationToken);

    private Task<MediaTimelineMutationOutcome> WriteAsync(FileMapping expectedMapping, string? expectedSeasonKey,
        Func<Models.ApplicationContext, Task> mutation, CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = new Models.ApplicationContext(options);
            await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
            // Serialize create/update/delete and season acceptance with each other
            // and with mapping changes, including the first insert for a key.
            await MappingTransactionLock.AcquireAsync(write, cancellationToken);
            var mapping = await write.FileMappings.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == expectedMapping.Id, cancellationToken);
            if (mapping is null) return MediaTimelineMutationOutcome.NotFound;
            if (mapping.ToRecord() != expectedMapping) return MediaTimelineMutationOutcome.Conflict;
            var info = await MappingTransactionLock.LockAnimationInfoAsync(
                write, expectedMapping.AnimationInfoId, cancellationToken);
            if (info?.IsDownloadFinished != true) return MediaTimelineMutationOutcome.NotFound;
            var animationId = write.Entry(info).Property<Guid?>("AnimationId").CurrentValue;
            var groupId = write.Entry(info).Property<Guid?>("GroupId").CurrentValue;
            var seasonKey = animationId.HasValue && groupId.HasValue && info.Season.HasValue
                ? $"season:{animationId}:{groupId}:{info.Season}" : null;
            if (seasonKey != expectedSeasonKey) return MediaTimelineMutationOutcome.Conflict;
            await mutation(write);
            await write.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return MediaTimelineMutationOutcome.Success;
        });

    private static MediaTimelineData? ToData(Models.MediaTimeline? row) => row is null ? null : new MediaTimelineData(row.Key,
        row.DurationSeconds, JsonSerializer.Deserialize(row.PointsJson, TimelineJsonContext.Default.MediaTimelinePointArray) ?? [], row.UpdatedAt);
}
