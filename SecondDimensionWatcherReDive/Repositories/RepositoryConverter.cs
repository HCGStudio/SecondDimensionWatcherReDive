using DataRepo = SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

internal static class RepositoryConverter
{
    // Entity -> Record

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
            entity.AiRetryCount);

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

    public static DataRepo.WebDavToken ToRecord(this Models.WebDavToken entity) =>
        new(entity.Id,
            entity.Username,
            entity.TokenHash,
            entity.Description,
            entity.CreatedAt);

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
            AiRetryCount = record.AiRetryCount
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

    public static Models.WebDavToken ToEntity(this DataRepo.WebDavToken record) =>
        new()
        {
            Id = record.Id,
            Username = record.Username,
            TokenHash = record.TokenHash,
            Description = record.Description,
            CreatedAt = record.CreatedAt
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
}
