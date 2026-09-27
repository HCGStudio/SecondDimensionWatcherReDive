using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class PasskeyCredential
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] CredentialId { get; set; } = [];
    public byte[] PublicKey { get; set; } = [];
    public long SignCount { get; set; }
    public string RelyingPartyId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

internal sealed class PasskeyCredentialConfiguration : IEntityTypeConfiguration<PasskeyCredential>
{
    public void Configure(EntityTypeBuilder<PasskeyCredential> builder)
    {
        builder.ToTable("PasskeyCredentials");
        builder.HasKey(value => value.Id);
        builder.HasIndex(value => value.CredentialId).IsUnique();
        builder.HasIndex(value => new { value.UserId, value.RelyingPartyId });
        builder.Property(value => value.CredentialId).HasMaxLength(1024);
        builder.Property(value => value.Name).HasMaxLength(64);
        builder.Property(value => value.RelyingPartyId).HasMaxLength(253);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(value => value.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
