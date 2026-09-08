using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SecondDimensionWatcherReDive.Models;

public sealed class MetadataRecognitionRule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public long Revision { get; set; }
    public Guid? SourceFeedId { get; set; }
    public string? TitlePattern { get; set; }
    public string? SubtitleGroup { get; set; }
    public string TmdbId { get; set; } = "";
    public int? FixedSeason { get; set; }
    public int EpisodeOffset { get; set; }
    public string? CanonicalGroupName { get; set; }
    public Guid? CreatedFromItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
}

public sealed class MetadataRecognitionHit
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = "";
    public long RuleRevision { get; set; }
    public Guid AnimationInfoId { get; set; }
    public string Title { get; set; } = "";
    public long ItemRevision { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}

public sealed class MetadataRecognitionRuleConfiguration : IEntityTypeConfiguration<MetadataRecognitionRule>
{
    public void Configure(EntityTypeBuilder<MetadataRecognitionRule> builder)
    {
        builder.ToTable("MetadataRecognitionRules");
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Name).HasMaxLength(200);
        builder.Property(rule => rule.TitlePattern).HasMaxLength(1000);
        builder.Property(rule => rule.SubtitleGroup).HasMaxLength(200);
        builder.Property(rule => rule.CanonicalGroupName).HasMaxLength(200);
        builder.Property(rule => rule.TmdbId).HasMaxLength(20);
        builder.Property(rule => rule.Revision).IsConcurrencyToken();
        builder.HasIndex(rule => new { rule.Enabled, rule.SourceFeedId });
    }
}

public sealed class MetadataRecognitionHitConfiguration : IEntityTypeConfiguration<MetadataRecognitionHit>
{
    public void Configure(EntityTypeBuilder<MetadataRecognitionHit> builder)
    {
        builder.ToTable("MetadataRecognitionHits");
        builder.HasKey(hit => hit.Id);
        builder.Property(hit => hit.RuleName).HasMaxLength(200);
        builder.HasIndex(hit => new { hit.AnimationInfoId, hit.ItemRevision }).IsUnique();
        builder.HasIndex(hit => hit.AppliedAt);
        builder.HasOne<AnimationInfo>().WithMany().HasForeignKey(hit => hit.AnimationInfoId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MetadataRecognitionRule>().WithMany().HasForeignKey(hit => hit.RuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
