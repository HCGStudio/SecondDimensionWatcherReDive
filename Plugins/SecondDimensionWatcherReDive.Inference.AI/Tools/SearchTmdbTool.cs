using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

[Tool<SearchTmdbParams>(
    "search_tmdb",
    "Search TMDB (The Movie Database) for an anime by name to get its TMDB ID and metadata.")]
internal sealed partial class SearchTmdbTool(TmdbTool tmdbTool) : ITool
{
    public async Task<IToolResult> ExecuteCoreAsync(
        SearchTmdbParams param, CancellationToken cancellationToken)
    {
        var result = await tmdbTool.SearchAsync(param.Query, cancellationToken);
        using var doc = JsonDocument.Parse(result);
        return new ToolSuccessResult<JsonElement>(doc.RootElement.Clone());
    }
}
