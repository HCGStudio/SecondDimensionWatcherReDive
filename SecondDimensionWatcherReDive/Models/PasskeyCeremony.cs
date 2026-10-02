using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class PasskeyCeremony
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string RelyingPartyId { get; set; } = string.Empty;
    public string BrowserBindingHash { get; set; } = string.Empty;
    public string OptionsJson { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class PasskeyCeremonyConfiguration : IEntityTypeConfiguration<PasskeyCeremony>
{
    public void Configure(EntityTypeBuilder<PasskeyCeremony> builder)
    {
        builder.ToTable("PasskeyCeremonies");
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => value.ExpiresAt);
        builder.Property(value => value.Purpose).HasMaxLength(32);
        builder.Property(value => value.Origin).HasMaxLength(512);
        builder.Property(value => value.RelyingPartyId).HasMaxLength(253);
        builder.Property(value => value.BrowserBindingHash).HasMaxLength(64);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<LoginSession>().WithMany().HasForeignKey(value => value.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
