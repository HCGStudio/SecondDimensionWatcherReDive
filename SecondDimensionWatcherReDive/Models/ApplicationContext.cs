using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
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
    public DbSet<AnimationCatalogEntry> AnimationCatalogEntries { get; set; }
    public DbSet<AnimationCatalogState> AnimationCatalogStates { get; set; }
    public DbSet<Feed> Feeds { get; set; }
    public DbSet<SeasonBangumi> SeasonBangumis { get; set; }
    public DbSet<BangumiSubgroup> BangumiSubgroups { get; set; }
    public DbSet<ChatConversation> ChatConversations { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    public DbSet<FileMapping> FileMappings { get; set; }
    public DbSet<FileSystemEntry> FileSystemEntries { get; set; }
    public DbSet<FileSystemDirectoryState> FileSystemDirectoryStates { get; set; }
    public DbSet<FileNameRegexRule> FileNameRegexRules { get; set; }
    public DbSet<SubscriptionAutomationPolicy> SubscriptionAutomationPolicies { get; set; }
    public DbSet<MigrationExecutionState> MigrationStates { get; set; }
    public DbSet<WebDavToken> WebDavTokens { get; set; }
    public DbSet<MetadataReviewOperation> MetadataReviewOperations { get; set; }
    public DbSet<MetadataReviewMappingSnapshot> MetadataReviewMappingSnapshots { get; set; }
    public DbSet<Incident> Incidents { get; set; }
    public DbSet<PlaybackProgress> PlaybackProgresses { get; set; }
    public DbSet<PlaybackPreference> PlaybackPreferences { get; set; }
    public DbSet<MediaLibrarySource> MediaLibrarySources { get; set; }
    public DbSet<ApplicationSettings> ApplicationSettings { get; set; }
    public DbSet<NotificationOutboxMessage> NotificationOutboxMessages { get; set; }
    public DbSet<TodoItemState> TodoItemStates { get; set; }
    public DbSet<WebPushSubscription> WebPushSubscriptions { get; set; }
    public DbSet<AuthenticationState> AuthenticationStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationOutboxMessage>()
            .HasIndex(message => message.DeduplicationKey)
            .IsUnique();

        modelBuilder.Entity<NotificationOutboxMessage>()
            .HasIndex(message => new { message.Status, message.NextAttemptAt });

        modelBuilder.Entity<NotificationOutboxMessage>()
            .HasIndex(message => message.WebPushSubscriptionId);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.DeduplicationKey)
            .HasMaxLength(256);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.Type)
            .HasConversion<string>()
            .HasMaxLength(48);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.Channel)
            .HasConversion<string>()
            .HasMaxLength(24);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.Status)
            .HasConversion<string>()
            .HasMaxLength(24);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.Title)
            .HasMaxLength(256);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.Body)
            .HasMaxLength(2048);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.DeepLink)
            .HasMaxLength(2048);

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.PayloadJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NotificationOutboxMessage>()
            .Property(message => message.LastError)
            .HasMaxLength(2048);

        modelBuilder.Entity<TodoItemState>()
            .HasKey(state => state.Key);

        modelBuilder.Entity<TodoItemState>()
            .Property(state => state.Key)
            .HasMaxLength(128);

        modelBuilder.Entity<WebPushSubscription>()
            .Property(subscription => subscription.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<WebPushSubscription>()
            .HasIndex(subscription => subscription.EndpointHash)
            .IsUnique();

        modelBuilder.Entity<WebPushSubscription>()
            .Property(subscription => subscription.EndpointHash)
            .HasMaxLength(64);

        modelBuilder.Entity<WebPushSubscription>()
            .Property(subscription => subscription.ProtectedEndpoint)
            .HasMaxLength(4096);

        modelBuilder.Entity<WebPushSubscription>()
            .Property(subscription => subscription.ProtectedP256Dh)
            .HasMaxLength(1024);

        modelBuilder.Entity<WebPushSubscription>()
            .Property(subscription => subscription.ProtectedAuth)
            .HasMaxLength(1024);

        modelBuilder.Entity<WebPushSubscription>()
            .Property(subscription => subscription.LastError)
            .HasMaxLength(256);

        modelBuilder.Entity<AuthenticationState>()
            .Property(state => state.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<AuthenticationState>()
            .Property(state => state.PasswordHash)
            .HasMaxLength(128);

        modelBuilder.Entity<AuthenticationState>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_AuthenticationStates_Singleton",
                "\"Id\" = 1"));
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

        modelBuilder.Entity<AnimationCatalogEntry>()
            .HasKey(entry => entry.AnimationId);

        modelBuilder.Entity<AnimationCatalogEntry>()
            .HasOne(entry => entry.Animation)
            .WithOne()
            .HasForeignKey<AnimationCatalogEntry>(entry => entry.AnimationId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AnimationCatalogEntry>()
            .HasIndex(entry => entry.TmdbId)
            .IsUnique();

        modelBuilder.Entity<AnimationCatalogEntry>()
            .HasIndex(entry => new { entry.LatestPublishTime, entry.TmdbId })
            .IsDescending();

        modelBuilder.Entity<AnimationCatalogEntry>()
            .ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AnimationCatalogEntries_Counts",
                    "\"EpisodeCount\" >= 0 AND \"ReleaseCount\" > 0 AND \"AutomationAttentionCount\" >= 0");
            });

        modelBuilder.Entity<AnimationCatalogState>()
            .HasKey(state => state.Id);

        modelBuilder.Entity<AnimationCatalogState>()
            .Property(state => state.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<AnimationCatalogState>()
            .ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_AnimationCatalogStates_Singleton",
                    "\"Id\" = 1");
                table.HasCheckConstraint(
                    "CK_AnimationCatalogStates_Revision_Positive",
                    "\"Revision\" > 0");
            });

        modelBuilder.Entity<AnimationGroup>()
            .HasIndex(group => group.Name)
            .IsUnique();

        modelBuilder.Entity<AnimationInfo>()
            .Property(info => info.StateVersion)
            .IsConcurrencyToken();

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
            .HasIndex("AnimationId", "PublishTime", "Id");

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => new { info.MediaLibraryMissingSince, info.PublishTime, info.Id });

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

        modelBuilder.Entity<MigrationExecutionState>(migration =>
        {
            migration.ToTable("MigrationMarkers");
            migration.HasKey(state => new { state.Key, state.Version });
            migration.Property(state => state.Key).HasMaxLength(256);
            migration.Property(state => state.Version).HasDefaultValue(1);
            migration.Property(state => state.Status)
                .HasDefaultValue(MigrationExecutionStatus.Completed)
                .HasSentinel((MigrationExecutionStatus)(-1));
            migration.Property(state => state.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            migration.Property(state => state.AttemptCount)
                .HasDefaultValue(1)
                .HasSentinel(-1);
            migration.Property(state => state.Checkpoint).HasMaxLength(4096);
            migration.Property(state => state.LastErrorSummary).HasMaxLength(4096);
            migration.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_MigrationMarkers_Version_Positive",
                    "\"Version\" > 0");
                table.HasCheckConstraint(
                    "CK_MigrationMarkers_AttemptCount_NonNegative",
                    "\"AttemptCount\" >= 0");
                table.HasCheckConstraint(
                    "CK_MigrationMarkers_Status_Range",
                    "\"Status\" BETWEEN 0 AND 3");
            });
        });

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

        modelBuilder.Entity<Incident>()
            .Property(incident => incident.Occurrence)
            .HasDefaultValue(1);

        modelBuilder.Entity<Incident>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_Incidents_Occurrence_Positive",
                "\"Occurrence\" > 0"));

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
            .Property(preference => preference.UserId)
            .ValueGeneratedNever();

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

        modelBuilder.Entity<FileMapping>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_FileMappings_VirtualPath_Canonical",
                "\"VirtualPath\" ~ '^/[^/]+(?:/[^/]+)*$' AND \"VirtualPath\" !~ '(^|/)\\.\\.?($|/)'"));

        modelBuilder.Entity<MetadataReviewMappingSnapshot>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_MetadataReviewMappingSnapshots_VirtualPath_Canonical",
                "\"VirtualPath\" ~ '^/[^/]+(?:/[^/]+)*$' AND \"VirtualPath\" !~ '(^|/)\\.\\.?($|/)'"));

        modelBuilder.Entity<FileSystemEntry>()
            .HasKey(entry => entry.Path);

        modelBuilder.Entity<FileSystemEntry>()
            .Property(entry => entry.EntryId)
            .HasDefaultValueSql("gen_random_uuid()");

        modelBuilder.Entity<FileSystemEntry>()
            .Property(entry => entry.Cookie)
            .HasDefaultValueSql("nextval('sdw_file_system_entry_cookie_seq')");

        modelBuilder.Entity<FileSystemEntry>()
            .HasIndex(entry => entry.EntryId)
            .IsUnique();

        modelBuilder.Entity<FileSystemEntry>()
            .HasIndex(entry => entry.Cookie)
            .IsUnique();

        modelBuilder.Entity<FileSystemEntry>()
            .HasIndex(entry => new { entry.ParentPath, entry.IsDirectory, entry.Name })
            .IsDescending(false, true, false);

        modelBuilder.Entity<FileSystemEntry>()
            .HasIndex(entry => new { entry.ParentPath, entry.Cookie });

        modelBuilder.Entity<FileSystemEntry>()
            .HasIndex(entry => entry.FileMappingId)
            .IsUnique()
            .HasFilter("\"FileMappingId\" IS NOT NULL");

        modelBuilder.Entity<FileSystemEntry>()
            .HasOne(entry => entry.FileMapping)
            .WithOne()
            .HasForeignKey<FileSystemEntry>(entry => entry.FileMappingId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FileSystemEntry>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_FileSystemEntries_NodeShape",
                "(\"IsDirectory\" AND \"FileMappingId\" IS NULL AND \"DescendantFileCount\" > 0) OR " +
                "(NOT \"IsDirectory\" AND \"FileMappingId\" IS NOT NULL AND \"DescendantFileCount\" = 1)"));

        modelBuilder.Entity<FileSystemDirectoryState>()
            .HasKey(state => state.Path);

        modelBuilder.Entity<FileSystemDirectoryState>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_FileSystemDirectoryStates_Generation_Positive",
                "\"Generation\" > 0"));

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
            .HasIndex(info => new { info.AutomationDisposition, info.PublishTime });

        modelBuilder.Entity<AnimationInfo>()
            .HasOne<Feed>()
            .WithMany()
            .HasForeignKey(info => info.SourceFeedId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => info.SourceFeedId);

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
