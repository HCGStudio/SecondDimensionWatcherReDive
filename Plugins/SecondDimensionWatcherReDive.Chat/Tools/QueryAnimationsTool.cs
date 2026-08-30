using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<QueryAnimationsParams>(
    "query_animations",
    "Query animation info list. Supports paged list, grouped by TMDB, downloading, downloaded, title search, and ID lookup. Grouped results return next_cursor when truncated; pass it back unchanged as grouped_cursor to continue.")]
internal sealed partial class QueryAnimationsTool(
    IAnimationInfoRepository animationInfoRepository) : ITool
{
    private async Task<IToolResult> ExecuteCoreAsync(
        QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        return param.Action switch
        {
            QueryAnimationsAction.List => new ToolSuccessResult<AnimationPagedResult>(
                await QueryListAsync(param, cancellationToken)),
            QueryAnimationsAction.Grouped => await QueryGroupedAsync(param, cancellationToken),
            QueryAnimationsAction.Downloading => new ToolSuccessResult<AnimationPagedResult>(
                await QueryDownloadingAsync(param, cancellationToken)),
            QueryAnimationsAction.Downloaded => new ToolSuccessResult<AnimationPagedResult>(
                await QueryDownloadedAsync(param, cancellationToken)),
            QueryAnimationsAction.SearchByTitle => await QueryByTitleAsync(param, cancellationToken),
            QueryAnimationsAction.GetById => await QueryByIdAsync(param, cancellationToken),
            _ => new ToolFailureResult($"Unknown action: {param.Action}")
        };
    }

    private async Task<AnimationPagedResult> QueryListAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var skip = param.Skip ?? 0;
        var take = param.Take ?? 10;
        var result = await animationInfoRepository.GetPagedAsync(skip, take, cancellationToken);
        return new AnimationPagedResult(result.TotalCount, result.Data.Select(ToSummary));
    }

    private async Task<IToolResult> QueryGroupedAsync(
        QueryAnimationsParams param,
        CancellationToken cancellationToken)
    {
        if (param.Skip is not null and not 0)
            return new ToolFailureResult(
                "skip is not supported for grouped queries; pass next_cursor from the previous result as grouped_cursor");

        var take = Math.Clamp(param.Take ?? 20, 1, 50);
        var cursor = param.GroupedCursor;
        var animationPage = cursor?.AnimationsComplete == true
            ? new AnimationCatalogPage([], null)
            : await animationInfoRepository.GetAnimationCatalogPageAsync(
                cursor?.Animations,
                take,
                cancellationToken);
        var uncategorizedPage = cursor?.UncategorizedComplete == true
            ? new AnimationInfoSummaryPage([], null)
            : await animationInfoRepository.GetUncategorizedPageAsync(
                cursor?.Uncategorized,
                take,
                cancellationToken);
        var animations = animationPage.Items.Select(item => new AnimationGroupItem(
                item.TmdbId,
                item.Name,
                item.OriginalName,
                item.PosterPath,
                item.EpisodeCount,
                item.ReleaseCount,
                item.AutomationAttentionCount))
            .ToList();
        var uncategorized = uncategorizedPage.Items.Select(ToSummary).ToList();
        var animationsComplete = cursor?.AnimationsComplete == true
                                 || animationPage.NextCursor is null;
        var uncategorizedComplete = cursor?.UncategorizedComplete == true
                                    || uncategorizedPage.NextCursor is null;
        var nextCursor = animationsComplete && uncategorizedComplete
            ? null
            : new AnimationGroupedCursor(
                animationPage.NextCursor,
                uncategorizedPage.NextCursor,
                animationsComplete,
                uncategorizedComplete);
        return new ToolSuccessResult<AnimationGroupedToolResult>(
            new AnimationGroupedToolResult(
                animations,
                animations.Count,
                uncategorized,
                uncategorized.Count,
                nextCursor,
                nextCursor is not null));
    }

    private async Task<AnimationPagedResult> QueryDownloadingAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var skip = param.Skip ?? 0;
        var take = param.Take ?? 10;
        var result = await animationInfoRepository.GetDownloadingPagedAsync(skip, take, cancellationToken);
        return new AnimationPagedResult(result.TotalCount, result.Data.Select(ToSummary));
    }

    private async Task<AnimationPagedResult> QueryDownloadedAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var skip = param.Skip ?? 0;
        var take = param.Take ?? 10;
        var result = await animationInfoRepository.GetDownloadedPagedAsync(skip, take, cancellationToken);
        return new AnimationPagedResult(result.TotalCount, result.Data.Select(ToSummary));
    }

    private async Task<IToolResult> QueryByTitleAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(param.Title))
            return new ToolFailureResult("title is required");

        var info = await animationInfoRepository.FindByTitleAsync(param.Title, cancellationToken);
        return new ToolSuccessResult<AnimationSearchResult>(info is null
            ? new AnimationSearchResult(false)
            : new AnimationSearchResult(true, ToSummary(info)));
    }

    private async Task<IToolResult> QueryByIdAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.Id, out var id))
            return new ToolFailureResult("Invalid or missing id");

        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);
        return new ToolSuccessResult<AnimationSearchResult>(info is null
            ? new AnimationSearchResult(false)
            : new AnimationSearchResult(true, ToSummary(info)));
    }

    private static AnimationSummary ToSummary(AnimationInfo info) => new(
        info.Id, info.Title, info.Season, info.Episode,
        info.IsDownloadTracked, info.IsDownloadFinished, info.IsAiProcessed,
        info.Animation?.Name, info.Group?.Name, info.PublishTime);

    private static AnimationSummary ToSummary(AnimationInfoSummary info) => new(
        info.Id, info.Title, info.Season, info.Episode,
        info.IsDownloadTracked, info.IsDownloadFinished, info.IsAiProcessed,
        info.AnimationName, info.GroupName, info.PublishTime);
}

internal enum QueryAnimationsAction
{
    List,
    Grouped,
    Downloading,
    Downloaded,
    SearchByTitle,
    GetById
}

internal sealed record QueryAnimationsParams(
    QueryAnimationsAction Action,
    int? Skip = null,
    int? Take = null,
    string? Title = null,
    string? Id = null,
    AnimationGroupedCursor? GroupedCursor = null);

internal sealed record AnimationPagedResult(int TotalCount, IEnumerable<AnimationSummary> Items);

internal sealed record AnimationSummary(
    Guid Id, string Title, int? Season, int? Episode,
    bool IsDownloadTracked, bool IsDownloadFinished, bool IsAiProcessed,
    string? AnimationName, string? GroupName, DateTimeOffset PublishTime);

internal sealed record AnimationGroupedToolResult(
    IEnumerable<AnimationGroupItem> Animations,
    int ReturnedAnimationCount,
    IEnumerable<AnimationSummary> Uncategorized,
    int ReturnedUncategorizedCount,
    AnimationGroupedCursor? NextCursor,
    bool IsTruncated);

internal sealed record AnimationGroupedCursor(
    AnimationCatalogCursor? Animations,
    AnimationInfoCursor? Uncategorized,
    bool AnimationsComplete,
    bool UncategorizedComplete);

internal sealed record AnimationGroupItem(
    string TmdbId, string Name, string OriginalName, string? PosterPath,
    int EpisodeCount, int ReleaseCount, int AutomationAttentionCount);

internal sealed record AnimationSearchResult(bool Found, AnimationSummary? Item = null);
