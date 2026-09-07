using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[Tool<GetTmdbSeasonEpisodesParams>(
    "get_tmdb_season_episodes",
    "Get individual episode details (episode number, name, air date, overview) for a specific season of a TV show. Use this when you need to verify episode mapping or resolve ambiguous numbering.",
    ToolRiskLevel.ReadOnly)]
internal sealed partial class GetTmdbSeasonEpisodesTool(TmdbTool tmdbTool) : ITool
{
    public async Task<IToolResult> ExecuteCoreAsync(
        GetTmdbSeasonEpisodesParams param, CancellationToken cancellationToken)
    {
        var result = await tmdbTool.GetSeasonEpisodesAsync(param.TmdbId, param.SeasonNumber, cancellationToken);
        using var doc = JsonDocument.Parse(result);
        return new ToolSuccessResult<JsonElement>(doc.RootElement.Clone());
    }
}
