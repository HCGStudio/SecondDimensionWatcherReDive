using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

internal sealed class TargetedTmdbToolExecutor(IToolExecutor inner, int tmdbId, int? targetSeason) : IToolExecutor
{
    public IReadOnlyList<ToolDefinition> ToolDefinitions => inner.ToolDefinitions;

    public Task<IToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (toolCall.Name is not ("get_tmdb_seasons" or "get_tmdb_season_episodes"))
            return Reject("Only the bound TMDB season tools are available for this inference.");
        try
        {
            using var document = JsonDocument.Parse(toolCall.Arguments);
            var arguments = document.RootElement;
            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty("tmdb_id", out var id)
                || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var requestedId)
                || requestedId != tmdbId)
                return Reject($"This inference is bound to TMDB {tmdbId}. Use that exact tmdb_id.");
            if (toolCall.Name == "get_tmdb_season_episodes" && targetSeason is { } season
                && (!arguments.TryGetProperty("season_number", out var number)
                    || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var requestedSeason)
                    || requestedSeason != season))
                return Reject($"This inference is bound to TMDB season {season}. Use that exact season_number.");
        }
        catch (JsonException)
        {
            return Reject("Provide a JSON object containing the bound TMDB tool arguments.");
        }
        return inner.ExecuteAsync(toolCall, cancellationToken);
    }

    private static Task<IToolResult> Reject(string message) =>
        Task.FromResult<IToolResult>(new ToolFailureResult(message));
}
