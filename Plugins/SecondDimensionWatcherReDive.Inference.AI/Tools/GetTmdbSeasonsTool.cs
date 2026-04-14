using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[Tool<GetTmdbSeasonsParams>(
    "get_tmdb_seasons",
    "Get the season/episode structure of a TV show from TMDB. Returns each season's episode_count. Use this after search_tmdb to check how seasons and episodes are organized, so you can normalize episode numbering.")]
internal sealed partial class GetTmdbSeasonsTool(TmdbTool tmdbTool) : ITool
{
    public async Task<IToolExecutionResult> ExecuteCoreAsync(
        GetTmdbSeasonsParams param, CancellationToken cancellationToken)
    {
        var result = await tmdbTool.GetSeasonsAsync(param.TmdbId, cancellationToken);
        return new ToolStringResult(result);
    }
}
