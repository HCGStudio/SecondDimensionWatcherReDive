using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Models;

public class ApplicationContext : DbContext
{
    public ApplicationContext(DbContextOptions<ApplicationContext> options)
        : base(options)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

#nullable disable
    public DbSet<Animation> Animations { get; set; }
    public DbSet<AnimationGroup> AnimationGroups { get; set; }
    public DbSet<AnimationInfo> AnimationInfo { get; set; }
    public DbSet<Feed> Feeds { get; set; }
    public DbSet<SeasonBangumi> SeasonBangumis { get; set; }
    public DbSet<BangumiSubgroup> BangumiSubgroups { get; set; }
    public DbSet<ChatConversation> ChatConversations { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<FileMapping> FileMappings { get; set; }
    public DbSet<FileNameRegexRule> FileNameRegexRules { get; set; }
    public DbSet<MigrationMarker> MigrationMarkers { get; set; }
    public DbSet<WebDavToken> WebDavTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationMarker>()
            .HasKey(m => m.Key);

        modelBuilder.Entity<WebDavToken>()
            .HasIndex(t => t.Username)
            .IsUnique();

        modelBuilder.Entity<SeasonBangumi>()
            .HasIndex(b => b.MikanId)
            .IsUnique();

        modelBuilder.Entity<FileMapping>()
            .HasIndex(m => m.VirtualPath)
            .IsUnique();

        modelBuilder.Entity<FileMapping>()
            .HasIndex(m => m.AnimationInfoId);

        modelBuilder.Entity<FileNameRegexRule>()
            .HasIndex(rule => new { rule.AnimationId, rule.Pattern })
            .IsUnique();

        modelBuilder.Entity<FileNameRegexRule>()
            .Property(rule => rule.Pattern)
            .HasMaxLength(FileNameRegexMatcher.MaxPatternLength);

        modelBuilder.Entity<FileNameRegexRule>()
            .HasOne<Animation>()
            .WithMany()
            .HasForeignKey(rule => rule.AnimationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FileNameRegexRule>()
            .HasIndex(rule => new { rule.AnimationId, rule.CreatedAt });

        modelBuilder.Entity<BangumiSubgroup>()
            .HasIndex(s => new { s.SeasonBangumiId, s.MikanSubgroupId })
            .IsUnique();

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.ConversationId);

        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
