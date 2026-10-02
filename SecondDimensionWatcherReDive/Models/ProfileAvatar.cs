using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class ProfileAvatar
{
    public Guid ProfileId { get; set; }
    public byte[] Data { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
}

internal sealed class ProfileAvatarConfiguration : IEntityTypeConfiguration<ProfileAvatar>
{
    public void Configure(EntityTypeBuilder<ProfileAvatar> builder)
    {
        builder.ToTable("ProfileAvatars");
        builder.HasKey(avatar => avatar.ProfileId);
        builder.Property(avatar => avatar.ProfileId).ValueGeneratedNever();
        builder.Property(avatar => avatar.ContentType).HasMaxLength(32);
        builder.HasOne<UserProfile>().WithOne().HasForeignKey<ProfileAvatar>(avatar => avatar.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
