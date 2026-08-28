using System.Text.Json;
using SecondDimensionWatcherReDive.AI.Abstractions;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record TextDelta(string Text) : IChatUpdate;

public sealed record ToolCallBegin(string Id, string Name) : IChatUpdate;

public sealed record ToolCallDelta(string Id, string ArgumentsDelta) : IChatUpdate;

public sealed record ToolResultUpdate(string ToolCallId, JsonElement Result) : IChatUpdate;

public sealed record Finished(string? StopReason) : IChatUpdate
{
    /// <summary>
    ///     Provider-owned state for the next tool round. The engine consumes it and never exposes it
    ///     through the public chat stream.
    /// </summary>
    internal IAIProviderContinuation? Continuation { get; init; }
}
