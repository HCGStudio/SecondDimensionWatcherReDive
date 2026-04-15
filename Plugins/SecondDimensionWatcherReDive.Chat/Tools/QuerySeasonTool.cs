using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
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
    private async Task<IToolResult> ExecuteCoreAsync(
        QuerySeasonParams param, CancellationToken cancellationToken)
    {
        return param.Action switch
        {
            QuerySeasonAction.CurrentSeason => new ToolSuccessResult<SeasonListResult>(
                await QueryCurrentSeasonAsync(cancellationToken)),
            QuerySeasonAction.BrowseSeason => new ToolSuccessResult<SeasonListResult>(
                await BrowseSeasonAsync(param, cancellationToken)),
            QuerySeasonAction.Subgroups => await QuerySubgroupsAsync(param, cancellationToken),
            _ => new ToolFailureResult($"Unknown action: {param.Action}")
        };
    }

    private async Task<SeasonListResult> QueryCurrentSeasonAsync(CancellationToken cancellationToken)
    {
        var bangumis = await seasonBangumiRepository.GetAllOrderedByDayAndTitleAsync(cancellationToken);
        return new SeasonListResult(
            bangumis.Count,
            bangumis.Select(b => new BangumiSummary(
                b.Id, b.MikanId, b.Title, b.DayOfWeek, b.ImageUrl)));
    }

    private async Task<SeasonListResult> BrowseSeasonAsync(QuerySeasonParams param, CancellationToken cancellationToken)
    {
        if (param.Year is null || param.Season is null)
            return await QueryCurrentSeasonAsync(cancellationToken);

        var bangumis = await seasonScraper.ScrapeSeasonAsync(param.Year.Value, param.Season.Value, cancellationToken);
        return new SeasonListResult(
            bangumis.Count,
            bangumis.Select(b => new BangumiSummary(b.Id, b.MikanId, b.Title, b.DayOfWeek, b.ImageUrl)));
    }

    private async Task<IToolResult> QuerySubgroupsAsync(QuerySeasonParams param, CancellationToken cancellationToken)
    {
        if (param.MikanId is null)
            return new ToolFailureResult("mikan_id is required");

        var bangumi = await seasonBangumiRepository.FindByMikanIdAsync(param.MikanId.Value, cancellationToken);
        if (bangumi is null)
            return new ToolFailureResult("Bangumi not found");

        var subgroups = await bangumiSubgroupRepository.GetBySeasonBangumiIdAsync(bangumi.Id, cancellationToken);
        return new ToolSuccessResult<SubgroupListResult>(new SubgroupListResult(
            bangumi.Title,
            subgroups.Select(s => new SubgroupSummary(s.MikanSubgroupId, s.Name))));
    }
}

internal enum QuerySeasonAction
{
    CurrentSeason,
    BrowseSeason,
    Subgroups
}

internal sealed record QuerySeasonParams(
    QuerySeasonAction Action,
    int? Year = null,
    AnimeSeason? Season = null,
    int? MikanId = null);

internal sealed record SeasonListResult(int Count, IEnumerable<BangumiSummary> Bangumis);
internal sealed record BangumiSummary(Guid Id, int MikanId, string Title, int DayOfWeek, string? ImageUrl);
internal sealed record SubgroupListResult(string BangumiTitle, IEnumerable<SubgroupSummary> Subgroups);
internal sealed record SubgroupSummary(int MikanSubgroupId, string Name);
