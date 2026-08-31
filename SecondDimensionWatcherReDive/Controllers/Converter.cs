using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Controllers;

internal static class Converter
{
    public static External.AnimationInfo ToExternal(this AnimationInfo record) =>
        new(record.Id,
            record.Title,
            record.Description,
            record.PublishTime,
            record.IsDownloadTracked,
            record.IsDownloadFinished,
            record.Season,
            record.Episode,
            record.Group?.ToExternal(),
            record.Animation?.ToExternal(),
            record.IsAiProcessed,
            record.SourceFeedId,
            record.ReleaseSizeBytes,
            record.AutomationDisposition?.ToString(),
            record.AutomationExplanationJson,
            string.Equals(
                record.DownloadType,
                FileDownloadTypes.MediaLibraryImport,
                StringComparison.Ordinal));

    public static External.Animation ToExternal(this Animation record) =>
        new(record.Name,
            record.OriginalName,
            record.TmdbId,
            record.PosterPath);

    public static External.AnimationGroup ToExternal(this AnimationGroup record) =>
        new(record.Name);

    public static External.AnimationInfo ToExternal(this AnimationInfoSummary record) =>
        new(record.Id,
            record.Title,
            record.Description,
            record.PublishTime,
            record.IsDownloadTracked,
            record.IsDownloadFinished,
            record.Season,
            record.Episode,
            record.GroupName is null ? null : new External.AnimationGroup(record.GroupName),
            record.AnimationTmdbId is null
                ? null
                : new External.Animation(
                    record.AnimationName ?? string.Empty,
                    record.AnimationOriginalName ?? string.Empty,
                    record.AnimationTmdbId,
                    record.AnimationPosterPath),
            record.IsAiProcessed,
            record.SourceFeedId,
            record.ReleaseSizeBytes,
            record.AutomationDisposition?.ToString(),
            record.AutomationExplanationJson,
            record.IsMediaLibraryImport);

    public static External.AnimationCatalogItem ToExternal(this AnimationCatalogItem result) =>
        new(result.TmdbId,
            result.Name,
            result.OriginalName,
            result.PosterPath,
            result.EpisodeCount,
            result.ReleaseCount,
            result.AutomationAttentionCount,
            result.LatestPublishTime);

    public static External.Feed ToExternal(this Feed record) =>
        new(record.Id, record.Url, record.Name, record.CreatedAt);

    public static External.SubscriptionAutomationPolicy ToExternal(
        this SubscriptionAutomationPolicy record) =>
        new(record.FeedId,
            record.SubtitleGroups,
            record.Resolutions,
            record.Codecs,
            record.Languages,
            record.MinSizeBytes,
            record.MaxSizeBytes,
            record.ExcludedKeywords,
            record.Mode.ToString(),
            record.CreatedAt,
            record.UpdatedAt);

    public static External.SubscriptionAutomationSimulationResult ToExternal(
        this Framework.Feed.SubscriptionAutomationSimulationResult result) =>
        new(result.Total,
            result.Matched,
            result.Entries.Select(entry => new External.SubscriptionAutomationSimulationEntry(
                entry.Id,
                entry.Title,
                entry.PublishedAt,
                entry.SizeBytes,
                entry.Matched,
                entry.Explanations.Select(explanation =>
                    new External.SubscriptionAutomationExplanation(
                        explanation.Field,
                        explanation.Passed,
                        explanation.Actual,
                        explanation.Expected,
                        explanation.Message)).ToList())).ToList());

    public static External.WebDavTokenSummary ToExternal(this WebDavToken record) =>
        new(record.Id, record.Username, record.Description, record.CreatedAt);

    public static External.SeasonBangumi ToExternal(this SeasonBangumi record) =>
        new(record.Id,
            record.MikanId,
            record.Title,
            record.DayOfWeek,
            record.ImageUrl,
            record.ScrapedAt);

    public static External.FileDownloadStatus ToExternal(this Data.FileDownloadStatus status) =>
        new(status.ItemId,
            status.Progress,
            status.Remaining,
            status.Speed,
            status.State);

    public static External.ResponseData<IEnumerable<External.AnimationInfo>> ToExternalResponseData(
        this IReadOnlyList<AnimationInfo> data, int totalCount) =>
        new(data.Select(d => d.ToExternal()), totalCount);

    public static External.ResponseData<List<External.AnimationInfo>> ToExternalListResponseData(
        this IReadOnlyList<AnimationInfo> data, int totalCount) =>
        new(data.Select(d => d.ToExternal()).ToList(), totalCount);
}
