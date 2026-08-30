using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.FileDownload;
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
    public DbSet<SubscriptionAutomationPolicy> SubscriptionAutomationPolicies { get; set; }
    public DbSet<MigrationMarker> MigrationMarkers { get; set; }
    public DbSet<WebDavToken> WebDavTokens { get; set; }
    public DbSet<MetadataReviewOperation> MetadataReviewOperations { get; set; }
    public DbSet<MetadataReviewMappingSnapshot> MetadataReviewMappingSnapshots { get; set; }
    public DbSet<Incident> Incidents { get; set; }
    public DbSet<PlaybackProgress> PlaybackProgresses { get; set; }
    public DbSet<PlaybackPreference> PlaybackPreferences { get; set; }
    public DbSet<MediaLibrarySource> MediaLibrarySources { get; set; }
    public DbSet<ApplicationSettings> ApplicationSettings { get; set; }
    public DbSet<ReleaseUpgradeOperation> ReleaseUpgradeOperations { get; set; }
    public DbSet<ReleaseUpgradeMappingSnapshot> ReleaseUpgradeMappingSnapshots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationSettings>()
            .Property(settings => settings.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<ApplicationSettings>()
            .Property(settings => settings.ValuesJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<ApplicationSettings>()
            .Property(settings => settings.Revision)
            .IsConcurrencyToken();

        modelBuilder.Entity<ApplicationSettings>()
            .ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_ApplicationSettings_Singleton",
                    "\"Id\" = 1");
                table.HasCheckConstraint(
                    "CK_ApplicationSettings_Revision_Positive",
                    "\"Revision\" > 0");
            });

        modelBuilder.Entity<Animation>()
            .HasIndex(animation => animation.TmdbId)
            .IsUnique();

        modelBuilder.Entity<AnimationGroup>()
            .HasIndex(group => group.Name)
            .IsUnique();

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.StateVersion)
            .IsConcurrencyToken();

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.IngestedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.MetadataLastError)
            .HasMaxLength(1024);

        modelBuilder.Entity<AnimationInfo>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_AnimationInfo_MetadataConfidence_Range",
                "\"MetadataConfidence\" IS NULL OR (\"MetadataConfidence\" >= 0 AND \"MetadataConfidence\" <= 1)"));

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => new { info.MetadataStatus, info.PublishTime });

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => info.ReleaseIdentity)
            .IsUnique()
            .HasFilter("\"ReleaseIdentity\" IS NOT NULL")
            .HasDatabaseName("UX_AnimationInfo_ReleaseIdentity");

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => new { info.Season, info.Episode, info.ReleaseScore });

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.ReleaseIdentity)
            .HasMaxLength(192);

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.FeedItemGuid)
            .HasMaxLength(1024);

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.EnclosureId)
            .HasMaxLength(2048);

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.TorrentInfoHash)
            .HasMaxLength(64);

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.ReleaseSubtitleGroup)
            .HasMaxLength(256);

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.ReleaseResolution)
            .HasMaxLength(32);

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.ReleaseCodec)
            .HasMaxLength(32);

        modelBuilder.Entity<AnimationInfo>()
            .ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AnimationInfo_ReleaseScore_NonNegative",
                    "\"ReleaseScore\" >= 0");
                table.HasCheckConstraint(
                    "CK_AnimationInfo_ExpectedEpisodeCount_Positive",
                    "\"ExpectedEpisodeCount\" IS NULL OR \"ExpectedEpisodeCount\" > 0");
            });

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => info.CurrentMetadataReviewOperationId)
            .IsUnique();

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => new { info.FileStore, info.StorePath })
            .IsUnique()
            .HasFilter($"\"DownloadType\" = '{FileDownloadTypes.MediaLibraryImport}'");

        modelBuilder.Entity<MediaLibrarySource>()
            .HasIndex(source => source.Path)
            .IsUnique();

        modelBuilder.Entity<MediaLibrarySource>()
            .Property(source => source.LastError)
            .HasMaxLength(2048);

        modelBuilder.Entity<AnimationInfo>()
            .HasOne<MediaLibrarySource>()
            .WithMany()
            .HasForeignKey(info => info.MediaLibrarySourceId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<MetadataReviewOperation>()
            .HasOne(operation => operation.AnimationInfo)
            .WithMany()
            .HasForeignKey(operation => operation.AnimationInfoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MetadataReviewOperation>()
            .HasIndex(operation => new { operation.AnimationInfoId, operation.State });

        modelBuilder.Entity<MetadataReviewOperation>()
            .HasIndex(operation => new { operation.State, operation.ExpiresAt });

        modelBuilder.Entity<MetadataReviewOperation>()
            .HasIndex(operation => new { operation.AnimationInfoId, operation.AppliedVersion })
            .IsUnique();

        modelBuilder.Entity<MetadataReviewOperation>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_MetadataReviewOperations_Expiry",
                "\"ExpiresAt\" > \"CreatedAt\""));

        modelBuilder.Entity<MetadataReviewMappingSnapshot>()
            .HasOne(snapshot => snapshot.Operation)
            .WithMany(operation => operation.MappingSnapshots)
            .HasForeignKey(snapshot => snapshot.OperationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MetadataReviewMappingSnapshot>()
            .HasIndex(snapshot => new { snapshot.OperationId, snapshot.Kind, snapshot.VirtualPath })
            .IsUnique();

        modelBuilder.Entity<MigrationMarker>()
            .HasKey(m => m.Key);

        modelBuilder.Entity<Incident>()
            .HasIndex(incident => incident.Fingerprint)
            .IsUnique();

        modelBuilder.Entity<Incident>()
            .HasIndex(incident => new { incident.ResolvedAt, incident.Type, incident.UpdatedAt });

        modelBuilder.Entity<Incident>()
            .Property(incident => incident.Fingerprint)
            .HasMaxLength(96);

        modelBuilder.Entity<Incident>()
            .Property(incident => incident.SourceId)
            .HasMaxLength(2048);

        modelBuilder.Entity<Incident>()
            .Property(incident => incident.Title)
            .HasMaxLength(256);

        modelBuilder.Entity<Incident>()
            .Property(incident => incident.Detail)
            .HasMaxLength(2048);

        modelBuilder.Entity<Incident>()
            .Property(incident => incident.LastRetryError)
            .HasMaxLength(2048);

        modelBuilder.Entity<WebDavToken>()
            .HasIndex(t => t.Username)
            .IsUnique();

        modelBuilder.Entity<PlaybackProgress>()
            .HasIndex(progress => new
            {
                progress.UserId,
                progress.AnimationInfoId,
                progress.VirtualPath
            })
            .IsUnique();

        modelBuilder.Entity<PlaybackProgress>()
            .HasIndex(progress => new { progress.UserId, progress.IsWatched, progress.UpdatedAt });

        modelBuilder.Entity<PlaybackProgress>()
            .Property(progress => progress.VirtualPath)
            .HasMaxLength(2048);

        modelBuilder.Entity<PlaybackProgress>()
            .HasOne(progress => progress.AnimationInfo)
            .WithMany()
            .HasForeignKey(progress => progress.AnimationInfoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlaybackProgress>()
            .ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PlaybackProgresses_Position_NonNegative",
                    "\"PositionSeconds\" >= 0");
                table.HasCheckConstraint(
                    "CK_PlaybackProgresses_Duration_NonNegative",
                    "\"DurationSeconds\" >= 0");
            });

        modelBuilder.Entity<PlaybackPreference>()
            .HasKey(preference => preference.UserId);

        modelBuilder.Entity<PlaybackPreference>()
            .Property(preference => preference.SubtitleLanguage)
            .HasMaxLength(64);

        modelBuilder.Entity<PlaybackPreference>()
            .Property(preference => preference.AudioLanguage)
            .HasMaxLength(64);

        modelBuilder.Entity<PlaybackPreference>()
            .Property(preference => preference.SubtitleTrackLabel)
            .HasMaxLength(128);

        modelBuilder.Entity<PlaybackPreference>()
            .Property(preference => preference.AudioTrackLabel)
            .HasMaxLength(128);

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

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.AutomationDisposition)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<AnimationInfo>()
            .HasOne<Feed>()
            .WithMany()
            .HasForeignKey(info => info.SourceFeedId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => info.SourceFeedId);

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex("AnimationId")
            .HasDatabaseName("IX_AnimationInfo_AnimationId");

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex("AnimationId", "Season", "Episode")
            .IsUnique()
            .HasFilter(
                "\"IsActiveRelease\" = TRUE AND \"AnimationId\" IS NOT NULL " +
                "AND \"Season\" IS NOT NULL AND \"Episode\" IS NOT NULL")
            .HasDatabaseName("UX_AnimationInfo_ActiveEpisodeRelease");

        modelBuilder.Entity<SubscriptionAutomationPolicy>()
            .HasKey(policy => policy.FeedId);

        modelBuilder.Entity<SubscriptionAutomationPolicy>()
            .Property(policy => policy.Mode)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<SubscriptionAutomationPolicy>()
            .HasOne(policy => policy.Feed)
            .WithOne()
            .HasForeignKey<SubscriptionAutomationPolicy>(policy => policy.FeedId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SubscriptionAutomationPolicy>()
            .HasIndex(policy => policy.UpdatedAt);

        modelBuilder.Entity<SubscriptionAutomationPolicy>()
            .ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_SubscriptionAutomationPolicies_MinimumUpgradeScore",
                    "\"MinimumUpgradeScore\" >= 1 AND \"MinimumUpgradeScore\" <= 1000");
                table.HasCheckConstraint(
                    "CK_SubscriptionAutomationPolicies_UpgradeRollbackHours",
                    "\"UpgradeRollbackHours\" >= 1 AND \"UpgradeRollbackHours\" <= 720");
            });

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .Property(operation => operation.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .Property(operation => operation.FailureSummary)
            .HasMaxLength(2048);

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .HasOne(operation => operation.CurrentRelease)
            .WithMany()
            .HasForeignKey(operation => operation.CurrentReleaseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .HasOne(operation => operation.CandidateRelease)
            .WithMany()
            .HasForeignKey(operation => operation.CandidateReleaseId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .HasIndex(operation => operation.CandidateReleaseId)
            .IsUnique()
            .HasFilter("\"Status\" <> 'Failed'");

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .HasIndex(operation => operation.CurrentReleaseId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Downloading', 'Verifying', 'Applied')")
            .HasDatabaseName("UX_ReleaseUpgradeOperations_ActiveCurrentRelease");

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .HasIndex(operation => new { operation.Status, operation.CreatedAt });

        modelBuilder.Entity<ReleaseUpgradeOperation>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_ReleaseUpgradeOperations_ScoreIncrease",
                "\"CandidateScore\" > \"CurrentScore\""));

        modelBuilder.Entity<ReleaseUpgradeMappingSnapshot>()
            .Property(snapshot => snapshot.Kind)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<ReleaseUpgradeMappingSnapshot>()
            .Property(snapshot => snapshot.VirtualPath)
            .HasMaxLength(2048);

        modelBuilder.Entity<ReleaseUpgradeMappingSnapshot>()
            .HasOne(snapshot => snapshot.Operation)
            .WithMany(operation => operation.MappingSnapshots)
            .HasForeignKey(snapshot => snapshot.OperationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReleaseUpgradeMappingSnapshot>()
            .HasIndex(snapshot => new { snapshot.OperationId, snapshot.Kind, snapshot.OriginalMappingId })
            .IsUnique();

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
