using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<QueryAnimationsParams>(
    "query_animations",
    "Query animation info list. Supports multiple query modes: paged list, grouped by TMDB, downloading, downloaded, search by title, and get by ID.")]
internal sealed partial class QueryAnimationsTool(
    IAnimationInfoRepository animationInfoRepository) : ITool
{
    private async Task<IToolExecutionResult> ExecuteCoreAsync(
        QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var result = param.Action switch
        {
            QueryAnimationsAction.List => await QueryListAsync(param, cancellationToken),
            QueryAnimationsAction.Grouped => await QueryGroupedAsync(cancellationToken),
            QueryAnimationsAction.Downloading => await QueryDownloadingAsync(param, cancellationToken),
            QueryAnimationsAction.Downloaded => await QueryDownloadedAsync(param, cancellationToken),
            QueryAnimationsAction.SearchByTitle => await QueryByTitleAsync(param, cancellationToken),
            QueryAnimationsAction.GetById => await QueryByIdAsync(param, cancellationToken),
            _ => ChatToolHelper.Serialize(new ToolError($"Unknown action: {param.Action}"))
        };
        return new ToolStringResult(result);
    }

    private async Task<string> QueryListAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var skip = param.Skip ?? 0;
        var take = param.Take ?? 10;
        var result = await animationInfoRepository.GetPagedAsync(skip, take, cancellationToken);
        return ChatToolHelper.Serialize(new AnimationPagedResult(result.TotalCount, result.Data.Select(ChatToolHelper.ToSummary)));
    }

    private async Task<string> QueryGroupedAsync(CancellationToken cancellationToken)
    {
        var result = await animationInfoRepository.GetGroupedAsync(cancellationToken);
        return ChatToolHelper.Serialize(new AnimationGroupedToolResult(
            result.Animations.Select(a => new AnimationGroupItem(
                a.TmdbId, a.Name, a.OriginalName, a.PosterPath, a.EpisodeCount,
                a.Episodes.Select(ChatToolHelper.ToSummary))),
            result.Uncategorized.Count,
            result.Uncategorized.Select(ChatToolHelper.ToSummary)));
    }

    private async Task<string> QueryDownloadingAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var skip = param.Skip ?? 0;
        var take = param.Take ?? 10;
        var result = await animationInfoRepository.GetDownloadingPagedAsync(skip, take, cancellationToken);
        return ChatToolHelper.Serialize(new AnimationPagedResult(result.TotalCount, result.Data.Select(ChatToolHelper.ToSummary)));
    }

    private async Task<string> QueryDownloadedAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        var skip = param.Skip ?? 0;
        var take = param.Take ?? 10;
        var result = await animationInfoRepository.GetDownloadedPagedAsync(skip, take, cancellationToken);
        return ChatToolHelper.Serialize(new AnimationPagedResult(result.TotalCount, result.Data.Select(ChatToolHelper.ToSummary)));
    }

    private async Task<string> QueryByTitleAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(param.Title))
            return ChatToolHelper.Serialize(new ToolError("title is required"));

        var info = await animationInfoRepository.FindByTitleAsync(param.Title, cancellationToken);
        return info is null
            ? ChatToolHelper.Serialize(new AnimationSearchResult(false))
            : ChatToolHelper.Serialize(new AnimationSearchResult(true, ChatToolHelper.ToSummary(info)));
    }

    private async Task<string> QueryByIdAsync(QueryAnimationsParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.Id, out var id))
            return ChatToolHelper.Serialize(new ToolError("Invalid or missing id"));

        var info = await animationInfoRepository.FindByIdAsync(id, cancellationToken);
        return info is null
            ? ChatToolHelper.Serialize(new AnimationSearchResult(false))
            : ChatToolHelper.Serialize(new AnimationSearchResult(true, ChatToolHelper.ToSummary(info)));
    }
}
