using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Abstractions;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed class ChatOptions
{
    public IToolExecutor? ToolExecutor { get; init; }

    /// <summary>
    ///     Maximum tool-execution budget. Built-in providers count model tool rounds; the Codex
    ///     app-server backend conservatively counts each dynamic tool call because its protocol
    ///     exposes calls inside a single streamed turn rather than provider-round boundaries.
    /// </summary>
    public int MaxToolRounds { get; init; } = 8;

    public int? MaxTokens { get; init; }

    /// <summary>Optional JSON schema constraining the final assistant response.</summary>
    public JsonElement? OutputSchema { get; init; }

    /// <summary>
    ///     Optional model override. If set, the engine uses this model instead of the configured default.
    /// </summary>
    public string? Model { get; init; }
}
