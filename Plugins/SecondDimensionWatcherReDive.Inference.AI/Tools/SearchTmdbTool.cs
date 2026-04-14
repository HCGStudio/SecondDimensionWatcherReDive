using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[Tool<SearchTmdbParams>(
    "search_tmdb",
    "Search TMDB (The Movie Database) for an anime by name to get its TMDB ID and metadata.")]
internal sealed partial class SearchTmdbTool(TmdbTool tmdbTool) : ITool
{
    public async Task<IToolExecutionResult> ExecuteCoreAsync(
        SearchTmdbParams param, CancellationToken cancellationToken)
    {
        var result = await tmdbTool.SearchAsync(param.Query, cancellationToken);
        return new ToolStringResult(result);
    }
}
