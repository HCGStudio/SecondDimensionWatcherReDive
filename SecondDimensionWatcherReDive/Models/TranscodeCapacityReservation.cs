using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class TranscodeCapacityReservation
{
    public Guid Id { get; set; }
    public string DirectoryPath { get; set; } = "";
    public string? VolumeIdentity { get; set; }
    public bool CountsAgainstDownloads { get; set; }
    public long BudgetBytes { get; set; }
    public long WrittenBytes { get; set; }
    public DateTimeOffset LeaseUntil { get; set; }
}

internal sealed class TranscodeCapacityReservationConfiguration : IEntityTypeConfiguration<TranscodeCapacityReservation>
{
    public void Configure(EntityTypeBuilder<TranscodeCapacityReservation> builder)
    {
        builder.ToTable("TranscodeCapacityReservations");
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.Id).ValueGeneratedNever();
        builder.Property(reservation => reservation.VolumeIdentity).HasMaxLength(512);
        builder.HasIndex(reservation => reservation.DirectoryPath).IsUnique();
        builder.HasIndex(reservation => reservation.LeaseUntil);
    }
}
