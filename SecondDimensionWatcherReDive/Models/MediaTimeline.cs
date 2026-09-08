using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class MediaTimeline
{
    public string Key { get; set; } = string.Empty;
    public Guid? MappingId { get; set; }
    public double DurationSeconds { get; set; }
    public string PointsJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class MediaTimelineBinding
{
    public string MediaVersion { get; set; } = string.Empty;
    public Guid MappingId { get; set; }
    public string SeasonKey { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public DateTimeOffset AcceptedRevision { get; set; }
}
internal sealed class MediaTimelineConfiguration : IEntityTypeConfiguration<MediaTimeline>
{
    public void Configure(EntityTypeBuilder<MediaTimeline> builder)
    {
        builder.ToTable("MediaTimelines"); builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(192);
        builder.Property(x => x.PointsJson).HasColumnType("jsonb");
        builder.HasOne<FileMapping>().WithMany().HasForeignKey(x => x.MappingId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class MediaTimelineBindingConfiguration : IEntityTypeConfiguration<MediaTimelineBinding>
{
    public void Configure(EntityTypeBuilder<MediaTimelineBinding> builder)
    {
        builder.ToTable("MediaTimelineBindings"); builder.HasKey(x => x.MediaVersion);
        builder.Property(x => x.MediaVersion).HasMaxLength(64);
        builder.Property(x => x.SeasonKey).HasMaxLength(192);
        builder.HasOne<FileMapping>().WithMany().HasForeignKey(x => x.MappingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaTimeline>().WithMany().HasForeignKey(x => x.SeasonKey).OnDelete(DeleteBehavior.Cascade);
    }
}
