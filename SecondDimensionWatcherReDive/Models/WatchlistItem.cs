using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class WatchlistItem
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string SubjectKey { get; set; } = string.Empty;
    public string? TmdbId { get; set; }
    public int? MikanId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "planned";
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class WatchlistItemConfiguration : IEntityTypeConfiguration<WatchlistItem>
{
    public void Configure(EntityTypeBuilder<WatchlistItem> builder)
    {
        builder.ToTable("WatchlistItems");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProfileId, x.SubjectKey }).IsUnique();
        builder.HasIndex(x => new { x.ProfileId, x.MikanId }).IsUnique();
        builder.Property(x => x.SubjectKey).HasMaxLength(96);
        builder.Property(x => x.TmdbId).HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(512);
        builder.Property(x => x.Status).HasMaxLength(16);
        builder.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}
