using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class TranscodeCacheReader
{
    public Guid Id { get; set; }
    public string DirectoryPath { get; set; } = "";
    public DateTimeOffset LeaseUntil { get; set; }
}

internal sealed class TranscodeCacheReaderConfiguration : IEntityTypeConfiguration<TranscodeCacheReader>
{
    public void Configure(EntityTypeBuilder<TranscodeCacheReader> builder)
    {
        builder.ToTable("TranscodeCacheReaders");
        builder.HasKey(reader => reader.Id);
        builder.Property(reader => reader.Id).ValueGeneratedNever();
        builder.HasIndex(reader => reader.DirectoryPath);
        builder.HasIndex(reader => reader.LeaseUntil);
    }
}
