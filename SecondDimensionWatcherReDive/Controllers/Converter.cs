using SecondDimensionWatcherReDive.Framework.DataRepository;

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
            record.IsAiProcessed);

    public static External.Animation ToExternal(this Animation record) =>
        new(record.Name,
            record.OriginalName,
            record.TmdbId,
            record.PosterPath);

    public static External.AnimationGroup ToExternal(this AnimationGroup record) =>
        new(record.Name);

    public static External.AnimationGroupedResponse ToExternal(this AnimationGroupedResult result) =>
        new(result.Animations.Select(a => a.ToExternal()).ToList(),
            result.Uncategorized.Select(i => i.ToExternal()).ToList());

    public static External.AnimationWithEpisodes ToExternal(this AnimationWithEpisodesResult result) =>
        new(result.TmdbId,
            result.Name,
            result.OriginalName,
            result.PosterPath,
            result.EpisodeCount,
            result.Episodes.Select(e => e.ToExternal()).ToList());

    public static External.Feed ToExternal(this Feed record) =>
        new(record.Id, record.Url, record.Name, record.CreatedAt);

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
