using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<QuerySeasonParams>(
    "query_season",
    "Query seasonal anime info. View current/past season anime lists, or list subgroups for a specific bangumi.")]
internal sealed partial class QuerySeasonTool(
    ISeasonBangumiRepository seasonBangumiRepository,
    IBangumiSubgroupRepository bangumiSubgroupRepository,
    ISeasonScraper seasonScraper) : ITool
{
    private async Task<IToolExecutionResult> ExecuteCoreAsync(
        QuerySeasonParams param, CancellationToken cancellationToken)
    {
        var result = param.Action switch
        {
            QuerySeasonAction.CurrentSeason => await QueryCurrentSeasonAsync(cancellationToken),
            QuerySeasonAction.BrowseSeason => await BrowseSeasonAsync(param, cancellationToken),
            QuerySeasonAction.Subgroups => await QuerySubgroupsAsync(param, cancellationToken),
            _ => ChatToolHelper.Serialize(new ToolError($"Unknown action: {param.Action}"))
        };
        return new ToolStringResult(result);
    }

    private async Task<string> QueryCurrentSeasonAsync(CancellationToken cancellationToken)
    {
        var bangumis = await seasonBangumiRepository.GetAllOrderedByDayAndTitleAsync(cancellationToken);
        return ChatToolHelper.Serialize(new SeasonListResult(
            bangumis.Count,
            bangumis.Select(b => new BangumiSummary(
                b.Id, b.MikanId, b.Title, b.DayOfWeek, b.ImageUrl))));
    }

    private async Task<string> BrowseSeasonAsync(QuerySeasonParams param, CancellationToken cancellationToken)
    {
        if (param.Year is null || param.Season is null)
            return await QueryCurrentSeasonAsync(cancellationToken);

        var bangumis = await seasonScraper.ScrapeSeasonAsync(param.Year.Value, param.Season.Value, cancellationToken);
        return ChatToolHelper.Serialize(new SeasonListResult(
            bangumis.Count,
            bangumis.Select(b => new BangumiSummary(b.Id, b.MikanId, b.Title, b.DayOfWeek, b.ImageUrl))));
    }

    private async Task<string> QuerySubgroupsAsync(QuerySeasonParams param, CancellationToken cancellationToken)
    {
        if (param.MikanId is null)
            return ChatToolHelper.Serialize(new ToolError("mikan_id is required"));

        var bangumi = await seasonBangumiRepository.FindByMikanIdAsync(param.MikanId.Value, cancellationToken);
        if (bangumi is null)
            return ChatToolHelper.Serialize(new ToolError("Bangumi not found"));

        var subgroups = await bangumiSubgroupRepository.GetBySeasonBangumiIdAsync(bangumi.Id, cancellationToken);
        return ChatToolHelper.Serialize(new SubgroupListResult(
            bangumi.Title,
            subgroups.Select(s => new SubgroupSummary(s.MikanSubgroupId, s.Name))));
    }
}
