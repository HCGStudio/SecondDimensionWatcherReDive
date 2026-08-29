using DataRepo = SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

internal static class RepositoryConverter
{
    // Entity -> Record

    public static DataRepo.ApplicationSettings ToRecord(this Models.ApplicationSettings entity) =>
        new(entity.Id,
            entity.ValuesJson,
            entity.ProtectedSecrets,
            entity.Revision,
            entity.UpdatedAt);

    public static DataRepo.AnimationInfo ToRecord(this Models.AnimationInfo entity) =>
        new(entity.Id,
            entity.Title,
            entity.Description,
            entity.PublishTime,
            entity.DownloadUrl,
            entity.DownloadType,
            entity.CachedDownloadData,
            entity.AdditionalDownloadInfo,
            entity.IsDownloadTracked,
            entity.DownloadStartTime,
            entity.DownloadEndTime,
            entity.IsDownloadFinished,
            entity.FileStore,
            entity.StorePath,
            entity.Season,
            entity.Episode,
            entity.Group?.ToRecord(),
            entity.Animation?.ToRecord(),
            entity.IsAiProcessed,
            entity.AiRetryCount,
            entity.SourceFeedId,
            entity.ReleaseSizeBytes,
            entity.AutomationDisposition,
            entity.AutomationExplanationJson,
            entity.MetadataStatus,
            entity.MetadataConfidence,
            entity.MetadataLastError,
            entity.MetadataReviewedAt,
            entity.StateVersion,
            entity.CurrentMetadataReviewOperationId,
            entity.DownloadAttemptId,
            entity.DownloadCancellationId,
            entity.MediaLibrarySourceId,
            entity.MediaLibraryMissingSince,
            entity.ReleaseIdentity,
            entity.FeedItemGuid,
            entity.EnclosureId,
            entity.TorrentInfoHash,
            entity.ReleaseSubtitleGroup,
            entity.ReleaseResolution,
            entity.ReleaseCodec,
            entity.ReleaseLanguages,
            entity.ReleaseScore,
            entity.ReleaseScoreReasonsJson,
            entity.ExpectedEpisodeCount,
            entity.IngestedAt,
            entity.IsActiveRelease);

    public static DataRepo.Animation ToRecord(this Models.Animation entity) =>
        new(entity.Id,
            entity.TmdbId,
            entity.Name,
            entity.OriginalName,
            entity.PosterPath);

    public static DataRepo.AnimationGroup ToRecord(this Models.AnimationGroup entity) =>
        new(entity.Id, entity.Name);

    public static DataRepo.Feed ToRecord(this Models.Feed entity) =>
        new(entity.Id, entity.Url, entity.Name, entity.CreatedAt);

    public static DataRepo.SeasonBangumi ToRecord(this Models.SeasonBangumi entity) =>
        new(entity.Id,
            entity.MikanId,
            entity.Title,
            entity.DayOfWeek,
            entity.ImageUrl,
            entity.ScrapedAt);

    public static DataRepo.BangumiSubgroup ToRecord(this Models.BangumiSubgroup entity) =>
        new(entity.Id,
            entity.SeasonBangumiId,
            entity.MikanSubgroupId,
            entity.Name,
            entity.ScrapedAt);

    public static DataRepo.FileMapping ToRecord(this Models.FileMapping entity) =>
        new(entity.Id,
            entity.AnimationInfoId,
            entity.VirtualPath,
            entity.PhysicalPath,
            entity.FileStore);

    public static DataRepo.FileNameRegexRule ToRecord(this Models.FileNameRegexRule entity) =>
        new(entity.Id,
            entity.AnimationId,
            entity.Pattern,
            entity.Description,
            entity.CreatedAt);

    public static DataRepo.SubscriptionAutomationPolicy ToRecord(
        this Models.SubscriptionAutomationPolicy entity) =>
        new(entity.FeedId,
            entity.SubtitleGroups,
            entity.Resolutions,
            entity.Codecs,
            entity.Languages,
            entity.MinSizeBytes,
            entity.MaxSizeBytes,
            entity.ExcludedKeywords,
            entity.Mode,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.EnableVersionUpgrade,
            entity.MinimumUpgradeScore,
            entity.UpgradeRollbackHours);

    public static DataRepo.WebDavToken ToRecord(this Models.WebDavToken entity) =>
        new(entity.Id,
            entity.Username,
            entity.TokenHash,
            entity.Description,
            entity.CreatedAt);

    public static DataRepo.PlaybackProgress ToRecord(this Models.PlaybackProgress entity) =>
        new(entity.Id,
            entity.UserId,
            entity.AnimationInfoId,
            entity.VirtualPath,
            entity.PositionSeconds,
            entity.DurationSeconds,
            entity.IsWatched,
            entity.UpdatedAt,
            entity.WatchedAt);

    public static DataRepo.PlaybackPreferences ToRecord(this Models.PlaybackPreference entity) =>
        new(entity.UserId,
            entity.SubtitleLanguage,
            entity.SubtitleTrackLabel,
            entity.AudioLanguage,
            entity.AudioTrackLabel,
            entity.AutoPlayNext,
            entity.UpdatedAt);

    // Record -> Entity

    public static Models.AnimationInfo ToEntity(this DataRepo.AnimationInfo record) =>
        new()
        {
            Id = record.Id,
            Title = record.Title,
            Description = record.Description,
            PublishTime = record.PublishTime,
            DownloadUrl = record.DownloadUrl,
            DownloadType = record.DownloadType,
            CachedDownloadData = record.CachedDownloadData,
            AdditionalDownloadInfo = record.AdditionalDownloadInfo,
            IsDownloadTracked = record.IsDownloadTracked,
            DownloadStartTime = record.DownloadStartTime,
            DownloadEndTime = record.DownloadEndTime,
            IsDownloadFinished = record.IsDownloadFinished,
            FileStore = record.FileStore,
            StorePath = record.StorePath,
            Season = record.Season,
            Episode = record.Episode,
            IsAiProcessed = record.IsAiProcessed,
            AiRetryCount = record.AiRetryCount,
            SourceFeedId = record.SourceFeedId,
            ReleaseSizeBytes = record.ReleaseSizeBytes,
            AutomationDisposition = record.AutomationDisposition,
            AutomationExplanationJson = record.AutomationExplanationJson,
            MetadataStatus = record.MetadataStatus,
            MetadataConfidence = record.MetadataConfidence,
            MetadataLastError = record.MetadataLastError,
            MetadataReviewedAt = record.MetadataReviewedAt,
            StateVersion = record.StateVersion,
            CurrentMetadataReviewOperationId = record.CurrentMetadataReviewOperationId,
            DownloadAttemptId = record.DownloadAttemptId,
            DownloadCancellationId = record.DownloadCancellationId,
            MediaLibrarySourceId = record.MediaLibrarySourceId,
            MediaLibraryMissingSince = record.MediaLibraryMissingSince,
            ReleaseIdentity = record.ReleaseIdentity,
            FeedItemGuid = record.FeedItemGuid,
            EnclosureId = record.EnclosureId,
            TorrentInfoHash = record.TorrentInfoHash,
            ReleaseSubtitleGroup = record.ReleaseSubtitleGroup,
            ReleaseResolution = record.ReleaseResolution,
            ReleaseCodec = record.ReleaseCodec,
            ReleaseLanguages = record.ReleaseLanguages?.ToArray() ?? [],
            ReleaseScore = record.ReleaseScore,
            ReleaseScoreReasonsJson = record.ReleaseScoreReasonsJson,
            ExpectedEpisodeCount = record.ExpectedEpisodeCount,
            IngestedAt = record.IngestedAt ?? DateTimeOffset.UtcNow,
            IsActiveRelease = record.IsActiveRelease
        };

    public static Models.Animation ToEntity(this DataRepo.Animation record) =>
        new()
        {
            Id = record.Id,
            TmdbId = record.TmdbId,
            Name = record.Name,
            OriginalName = record.OriginalName,
            PosterPath = record.PosterPath
        };

    public static Models.AnimationGroup ToEntity(this DataRepo.AnimationGroup record) =>
        new()
        {
            Id = record.Id,
            Name = record.Name
        };

    public static Models.Feed ToEntity(this DataRepo.Feed record) =>
        new()
        {
            Id = record.Id,
            Url = record.Url,
            Name = record.Name,
            CreatedAt = record.CreatedAt
        };

    public static Models.SeasonBangumi ToEntity(this DataRepo.SeasonBangumi record) =>
        new()
        {
            Id = record.Id,
            MikanId = record.MikanId,
            Title = record.Title,
            DayOfWeek = record.DayOfWeek,
            ImageUrl = record.ImageUrl,
            ScrapedAt = record.ScrapedAt
        };

    public static Models.BangumiSubgroup ToEntity(this DataRepo.BangumiSubgroup record) =>
        new()
        {
            Id = record.Id,
            SeasonBangumiId = record.SeasonBangumiId,
            MikanSubgroupId = record.MikanSubgroupId,
            Name = record.Name,
            ScrapedAt = record.ScrapedAt
        };

    public static Models.FileMapping ToEntity(this DataRepo.FileMapping record) =>
        new()
        {
            Id = record.Id,
            AnimationInfoId = record.AnimationInfoId,
            VirtualPath = record.VirtualPath,
            PhysicalPath = record.PhysicalPath,
            FileStore = record.FileStore
        };

    public static Models.FileNameRegexRule ToEntity(this DataRepo.FileNameRegexRule record) =>
        new()
        {
            Id = record.Id,
            AnimationId = record.AnimationId,
            Pattern = record.Pattern,
            Description = record.Description,
            CreatedAt = record.CreatedAt
        };

    public static Models.SubscriptionAutomationPolicy ToEntity(
        this DataRepo.SubscriptionAutomationPolicy record) =>
        new()
        {
            FeedId = record.FeedId,
            SubtitleGroups = record.SubtitleGroups.ToArray(),
            Resolutions = record.Resolutions.ToArray(),
            Codecs = record.Codecs.ToArray(),
            Languages = record.Languages.ToArray(),
            MinSizeBytes = record.MinSizeBytes,
            MaxSizeBytes = record.MaxSizeBytes,
            ExcludedKeywords = record.ExcludedKeywords.ToArray(),
            Mode = record.Mode,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            EnableVersionUpgrade = record.EnableVersionUpgrade,
            MinimumUpgradeScore = record.MinimumUpgradeScore,
            UpgradeRollbackHours = record.UpgradeRollbackHours
        };

    public static Models.WebDavToken ToEntity(this DataRepo.WebDavToken record) =>
        new()
        {
            Id = record.Id,
            Username = record.Username,
            TokenHash = record.TokenHash,
            Description = record.Description,
            CreatedAt = record.CreatedAt
        };

    public static Models.PlaybackProgress ToEntity(this DataRepo.PlaybackProgress record) =>
        new()
        {
            Id = record.Id,
            UserId = record.UserId,
            AnimationInfoId = record.AnimationInfoId,
            VirtualPath = record.VirtualPath,
            PositionSeconds = record.PositionSeconds,
            DurationSeconds = record.DurationSeconds,
            IsWatched = record.IsWatched,
            UpdatedAt = record.UpdatedAt,
            WatchedAt = record.WatchedAt
        };

    public static Models.PlaybackPreference ToEntity(this DataRepo.PlaybackPreferences record) =>
        new()
        {
            UserId = record.UserId,
            SubtitleLanguage = record.SubtitleLanguage,
            SubtitleTrackLabel = record.SubtitleTrackLabel,
            AudioLanguage = record.AudioLanguage,
            AudioTrackLabel = record.AudioTrackLabel,
            AutoPlayNext = record.AutoPlayNext,
            UpdatedAt = record.UpdatedAt
        };

    // Record -> Entity updater (apply record properties to tracked entity)

    public static void ApplyTo(this DataRepo.AnimationInfo record, Models.AnimationInfo entity)
    {
        entity.Title = record.Title;
        entity.Description = record.Description;
        entity.PublishTime = record.PublishTime;
        entity.DownloadUrl = record.DownloadUrl;
        entity.DownloadType = record.DownloadType;
        entity.CachedDownloadData = record.CachedDownloadData;
        entity.AdditionalDownloadInfo = record.AdditionalDownloadInfo;
        entity.IsDownloadTracked = record.IsDownloadTracked;
        entity.DownloadStartTime = record.DownloadStartTime;
        entity.DownloadEndTime = record.DownloadEndTime;
        entity.IsDownloadFinished = record.IsDownloadFinished;
        entity.FileStore = record.FileStore;
        entity.StorePath = record.StorePath;
        entity.Season = record.Season;
        entity.Episode = record.Episode;
        entity.IsAiProcessed = record.IsAiProcessed;
        entity.AiRetryCount = record.AiRetryCount;
        entity.SourceFeedId = record.SourceFeedId;
        entity.ReleaseSizeBytes = record.ReleaseSizeBytes;
        entity.AutomationDisposition = record.AutomationDisposition;
        entity.AutomationExplanationJson = record.AutomationExplanationJson;
        entity.MetadataStatus = record.MetadataStatus;
        entity.MetadataConfidence = record.MetadataConfidence;
        entity.MetadataLastError = record.MetadataLastError;
        entity.MetadataReviewedAt = record.MetadataReviewedAt;
        entity.StateVersion = record.StateVersion;
        entity.CurrentMetadataReviewOperationId = record.CurrentMetadataReviewOperationId;
        entity.DownloadAttemptId = record.DownloadAttemptId;
        entity.DownloadCancellationId = record.DownloadCancellationId;
        entity.MediaLibrarySourceId = record.MediaLibrarySourceId;
        entity.MediaLibraryMissingSince = record.MediaLibraryMissingSince;
        entity.ReleaseIdentity = record.ReleaseIdentity;
        entity.FeedItemGuid = record.FeedItemGuid;
        entity.EnclosureId = record.EnclosureId;
        entity.TorrentInfoHash = record.TorrentInfoHash;
        entity.ReleaseSubtitleGroup = record.ReleaseSubtitleGroup;
        entity.ReleaseResolution = record.ReleaseResolution;
        entity.ReleaseCodec = record.ReleaseCodec;
        entity.ReleaseLanguages = record.ReleaseLanguages?.ToArray() ?? [];
        entity.ReleaseScore = record.ReleaseScore;
        entity.ReleaseScoreReasonsJson = record.ReleaseScoreReasonsJson;
        entity.ExpectedEpisodeCount = record.ExpectedEpisodeCount;
    }

    public static void ApplyTo(this DataRepo.SeasonBangumi record, Models.SeasonBangumi entity)
    {
        entity.MikanId = record.MikanId;
        entity.Title = record.Title;
        entity.DayOfWeek = record.DayOfWeek;
        entity.ImageUrl = record.ImageUrl;
        entity.ScrapedAt = record.ScrapedAt;
    }

    public static void ApplyTo(this DataRepo.BangumiSubgroup record, Models.BangumiSubgroup entity)
    {
        entity.SeasonBangumiId = record.SeasonBangumiId;
        entity.MikanSubgroupId = record.MikanSubgroupId;
        entity.Name = record.Name;
        entity.ScrapedAt = record.ScrapedAt;
    }

    public static void ApplyTo(
        this DataRepo.SubscriptionAutomationPolicy record,
        Models.SubscriptionAutomationPolicy entity)
    {
        entity.SubtitleGroups = record.SubtitleGroups.ToArray();
        entity.Resolutions = record.Resolutions.ToArray();
        entity.Codecs = record.Codecs.ToArray();
        entity.Languages = record.Languages.ToArray();
        entity.MinSizeBytes = record.MinSizeBytes;
        entity.MaxSizeBytes = record.MaxSizeBytes;
        entity.ExcludedKeywords = record.ExcludedKeywords.ToArray();
        entity.Mode = record.Mode;
        entity.CreatedAt = record.CreatedAt;
        entity.UpdatedAt = record.UpdatedAt;
        entity.EnableVersionUpgrade = record.EnableVersionUpgrade;
        entity.MinimumUpgradeScore = record.MinimumUpgradeScore;
        entity.UpgradeRollbackHours = record.UpgradeRollbackHours;
    }
}
