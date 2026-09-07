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
    public DbSet<ChatPendingAction> ChatPendingActions { get; set; }
    public DbSet<ChatActionAudit> ChatActionAudits { get; set; }
    public DbSet<FileMapping> FileMappings { get; set; }
    public DbSet<StagedFileMapping> StagedFileMappings { get; set; }
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
    public DbSet<PlaybackCatalogState> PlaybackCatalogStates { get; set; }
    public DbSet<PlaybackPreference> PlaybackPreferences { get; set; }
    public DbSet<MediaLibrarySource> MediaLibrarySources { get; set; }
    public DbSet<ApplicationSettings> ApplicationSettings { get; set; }
    public DbSet<UserAccount> Users { get; set; }
    public DbSet<UserProfile> Profiles { get; set; }
    public DbSet<LoginSession> LoginSessions { get; set; }

    public DbSet<DurableJob> DurableJobs { get; set; }
    public DbSet<ScheduledTaskState> ScheduledTaskStates { get; set; }

    public DbSet<ReleaseUpgradeOperation> ReleaseUpgradeOperations { get; set; }
    public DbSet<ReleaseUpgradeMappingSnapshot> ReleaseUpgradeMappingSnapshots { get; set; }
    public DbSet<NotificationOutboxMessage> NotificationOutboxMessages { get; set; }
    public DbSet<TodoItemState> TodoItemStates { get; set; }
    public DbSet<WebPushSubscription> WebPushSubscriptions { get; set; }
    public DbSet<AuthenticationState> AuthenticationStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AnimationInfo).Assembly);
        modelBuilder.Entity<UserAccount>()
            .HasIndex(user => user.Username)
            .IsUnique();

        modelBuilder.Entity<UserAccount>()
            .Property(user => user.Username)
            .HasMaxLength(64);

        modelBuilder.Entity<UserAccount>()
            .Property(user => user.PasswordHash)
            .HasMaxLength(128);

        modelBuilder.Entity<UserAccount>()
            .Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(16);

        modelBuilder.Entity<UserProfile>()
            .Property(profile => profile.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<UserProfile>()
            .Property(profile => profile.Name)
            .HasMaxLength(64);

        modelBuilder.Entity<UserProfile>()
            .Property(profile => profile.Avatar)
            .HasMaxLength(512);

        modelBuilder.Entity<UserProfile>()
            .Property(profile => profile.PinHash)
            .HasMaxLength(128);

        modelBuilder.Entity<UserProfile>()
            .HasIndex(profile => new { profile.UserId, profile.Name })
            .IsUnique();

        modelBuilder.Entity<UserProfile>()
            .HasOne(profile => profile.User)
            .WithMany(user => user.Profiles)
            .HasForeignKey(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LoginSession>()
            .Property(session => session.RefreshTokenHash)
            .HasMaxLength(64);

        modelBuilder.Entity<LoginSession>()
            .Property(session => session.DeviceName)
            .HasMaxLength(128);

        modelBuilder.Entity<LoginSession>()
            .HasIndex(session => new { session.UserId, session.RevokedAt, session.ExpiresAt });

        modelBuilder.Entity<LoginSession>()
            .HasOne(session => session.User)
            .WithMany(user => user.Sessions)
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LoginSession>()
            .HasOne(session => session.ActiveProfile)
            .WithMany()
            .HasForeignKey(session => session.ActiveProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DurableJob>()
            .HasIndex(job => job.DeduplicationKey)
            .IsUnique();

        modelBuilder.Entity<DurableJob>()
            .HasIndex(job => new { job.Status, job.NextAttemptAt, job.LeaseExpiresAt });

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.DeduplicationKey)
            .HasMaxLength(256);

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.Type)
            .HasConversion<string>()
            .HasMaxLength(48);

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.Status)
            .HasConversion<string>()
            .HasMaxLength(24);

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.Stage)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.PayloadJson)
            .HasColumnType("jsonb");

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.LeaseOwner)
            .HasMaxLength(128);

        modelBuilder.Entity<DurableJob>()
            .Property(job => job.LastError)
            .HasMaxLength(512);

        modelBuilder.Entity<ScheduledTaskState>()
            .HasKey(state => state.TaskId);

        modelBuilder.Entity<ScheduledTaskState>()
            .Property(state => state.TaskId)
            .HasMaxLength(128);

        modelBuilder.Entity<ScheduledTaskState>()
            .Property(state => state.LeaseOwner)
            .HasMaxLength(128);

        modelBuilder.Entity<ScheduledTaskState>()
            .Property(state => state.LastError)
            .HasMaxLength(256);

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
            .HasIndex(info => info.ReleaseSubtitleGroup);

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => info.ReleaseResolution);

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => info.ReleaseCodec);

        modelBuilder.Entity<AnimationInfo>()
            .HasIndex(info => new
            {
                info.DownloadType,
                info.IsDownloadFinished,
                info.IsDownloadTracked
            });

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

        modelBuilder.Entity<WebDavToken>()
            .Property(token => token.Scope)
            .HasMaxLength(32);

        modelBuilder.Entity<WebDavToken>()
            .Property(token => token.VirtualRoot)
            .HasMaxLength(2048);

        modelBuilder.Entity<WebDavToken>()
            .HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

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
            .HasOne(progress => progress.Profile)
            .WithMany()
            .HasForeignKey(progress => progress.UserId)
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

        modelBuilder.Entity<PlaybackCatalogState>()
            .HasKey(state => state.UserId);

        modelBuilder.Entity<PlaybackCatalogState>()
            .Property(state => state.UserId)
            .ValueGeneratedNever();

        modelBuilder.Entity<PlaybackCatalogState>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_PlaybackCatalogStates_Revision_Positive",
                "\"Revision\" > 0"));

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

        modelBuilder.Entity<PlaybackPreference>()
            .HasOne(preference => preference.Profile)
            .WithOne()
            .HasForeignKey<PlaybackPreference>(preference => preference.UserId)
            .OnDelete(DeleteBehavior.Cascade);

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

        modelBuilder.Entity<StagedFileMapping>()
            .HasIndex(mapping => new { mapping.AnimationInfoId, mapping.VirtualPath })
            .IsUnique();

        modelBuilder.Entity<StagedFileMapping>()
            .Property(mapping => mapping.VirtualPath)
            .HasMaxLength(2048);

        modelBuilder.Entity<StagedFileMapping>()
            .HasOne<AnimationInfo>()
            .WithMany()
            .HasForeignKey(mapping => mapping.AnimationInfoId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StagedFileMapping>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_StagedFileMappings_VirtualPath_Canonical",
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
            .Property(policy => policy.MinimumUpgradeScore)
            .HasDefaultValue(25);

        modelBuilder.Entity<SubscriptionAutomationPolicy>()
            .Property(policy => policy.UpgradeRollbackHours)
            .HasDefaultValue(72);

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

        modelBuilder.Entity<ReleaseUpgradeMappingSnapshot>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_ReleaseUpgradeMappingSnapshots_VirtualPath_Canonical",
                "\"VirtualPath\" ~ '^/[^/]+(?:/[^/]+)*$' AND \"VirtualPath\" !~ '(^|/)\\.\\.?($|/)'"));

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

        modelBuilder.Entity<ChatConversation>()
            .HasIndex(conversation => new { conversation.ProfileId, conversation.UpdatedAt });

        modelBuilder.Entity<ChatConversation>()
            .HasOne(conversation => conversation.Profile)
            .WithMany()
            .HasForeignKey(conversation => conversation.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ChatPendingAction>()
            .HasIndex(action => new { action.UserId, action.ConversationId, action.ToolCallId });

        modelBuilder.Entity<ChatPendingAction>()
            .HasIndex(action => new { action.ConversationId, action.ToolCallId });

        modelBuilder.Entity<ChatPendingAction>()
            .HasIndex(action => new { action.UserId, action.ConversationId, action.State });

        modelBuilder.Entity<ChatPendingAction>()
            .HasIndex(action => new { action.State, action.ExpiresAt });

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.RiskLevel)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.State)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ToolCallId)
            .HasMaxLength(256);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ToolName)
            .HasMaxLength(128);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ParameterHash)
            .HasMaxLength(64);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ApprovalTokenHash)
            .HasMaxLength(64);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ParameterSummary)
            .HasMaxLength(1024);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ImpactSummary)
            .HasMaxLength(2048);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ResultSummary)
            .HasMaxLength(1024);

        modelBuilder.Entity<ChatPendingAction>()
            .Property(action => action.ErrorSummary)
            .HasMaxLength(1024);

        modelBuilder.Entity<ChatPendingAction>()
            .ToTable(table => table.HasCheckConstraint(
                "CK_ChatPendingActions_Expiry",
                "\"ExpiresAt\" > \"CreatedAt\""));

        modelBuilder.Entity<ChatActionAudit>()
            .HasOne(audit => audit.Action)
            .WithMany(action => action.AuditEntries)
            .HasForeignKey(audit => audit.ActionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ChatActionAudit>()
            .HasIndex(audit => new { audit.UserId, audit.ConversationId, audit.CreatedAt });

        modelBuilder.Entity<ChatActionAudit>()
            .Property(audit => audit.RiskLevel)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<ChatActionAudit>()
            .Property(audit => audit.Event)
            .HasConversion<string>()
            .HasMaxLength(32);

        modelBuilder.Entity<ChatActionAudit>()
            .Property(audit => audit.ToolName)
            .HasMaxLength(128);

        modelBuilder.Entity<ChatActionAudit>()
            .Property(audit => audit.ParameterHash)
            .HasMaxLength(64);

        modelBuilder.Entity<ChatActionAudit>()
            .Property(audit => audit.ParameterSummary)
            .HasMaxLength(1024);

        modelBuilder.Entity<ChatActionAudit>()
            .Property(audit => audit.Detail)
            .HasMaxLength(1024);
    }
}
