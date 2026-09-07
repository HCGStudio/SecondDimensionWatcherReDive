using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class DownloadCapacityEntry
{
    public Guid ItemId { get; set; }
    public Guid? DownloadAttemptId { get; set; }
    public string Title { get; set; } = "";
    public string Hash { get; set; } = "";
    public long? ExpectedBytes { get; set; }
    public long RemainingBytes { get; set; }
    public string State { get; set; } = "Waiting";
    public bool Paused { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class DownloadCapacityEntryConfiguration : IEntityTypeConfiguration<DownloadCapacityEntry>
{
    public void Configure(EntityTypeBuilder<DownloadCapacityEntry> builder)
    {
        builder.ToTable("DownloadCapacityEntries");
        builder.HasKey(entry => entry.ItemId);
        builder.Property(entry => entry.ItemId).ValueGeneratedNever();
        builder.Property(entry => entry.Hash).HasMaxLength(128);
        builder.Property(entry => entry.State).HasMaxLength(32);
        builder.Property(entry => entry.Reason).HasMaxLength(1024);
        builder.HasIndex(entry => new { entry.State, entry.CreatedAt });
        builder.HasOne<AnimationInfo>().WithOne().HasForeignKey<DownloadCapacityEntry>(entry => entry.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
