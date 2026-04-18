using Microsoft.EntityFrameworkCore;

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
    public DbSet<MigrationMarker> MigrationMarkers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationMarker>()
            .HasKey(m => m.Key);


        modelBuilder.Entity<SeasonBangumi>()
            .HasIndex(b => b.MikanId)
            .IsUnique();

        modelBuilder.Entity<FileMapping>()
            .HasIndex(m => m.VirtualPath)
            .IsUnique();

        modelBuilder.Entity<FileMapping>()
            .HasIndex(m => m.AnimationInfoId);

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
