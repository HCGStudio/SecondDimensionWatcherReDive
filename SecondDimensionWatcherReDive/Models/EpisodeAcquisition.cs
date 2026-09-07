using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace SecondDimensionWatcherReDive.Models;

public sealed class EpisodeAcquisition
{
    public string TmdbId { get; set; } = "";
    public int Season { get; set; }
    public int Episode { get; set; }
    public Guid ClaimId { get; set; }
    public Guid ReleaseId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class EpisodeAcquisitionConfiguration : IEntityTypeConfiguration<EpisodeAcquisition>
{
    public void Configure(EntityTypeBuilder<EpisodeAcquisition> builder)
    {
        builder.ToTable("EpisodeAcquisitions");
        builder.HasKey(x => new { x.TmdbId, x.Season, x.Episode });
        builder.Property(x => x.TmdbId).HasMaxLength(32);
    }
}
