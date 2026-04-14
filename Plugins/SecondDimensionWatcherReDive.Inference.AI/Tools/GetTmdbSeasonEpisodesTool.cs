using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[Tool<GetTmdbSeasonEpisodesParams>(
    "get_tmdb_season_episodes",
    "Get individual episode details (episode number, name, air date, overview) for a specific season of a TV show. Use this when you need to verify episode mapping or resolve ambiguous numbering.")]
internal sealed partial class GetTmdbSeasonEpisodesTool(TmdbTool tmdbTool) : ITool
{
    public async Task<IToolExecutionResult> ExecuteCoreAsync(
        GetTmdbSeasonEpisodesParams param, CancellationToken cancellationToken)
    {
        var result = await tmdbTool.GetSeasonEpisodesAsync(param.TmdbId, param.SeasonNumber, cancellationToken);
        return new ToolStringResult(result);
    }
}
