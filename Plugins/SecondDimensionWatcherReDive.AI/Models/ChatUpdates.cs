using SecondDimensionWatcherReDive.AI.Abstractions;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record TextDelta(string Text) : IChatUpdate;

public sealed record ToolCallBegin(string Id, string Name) : IChatUpdate;

public sealed record ToolCallDelta(string Id, string ArgumentsDelta) : IChatUpdate;

public sealed record ToolResultUpdate(string ToolCallId, string Result) : IChatUpdate;

public sealed record Finished(string? StopReason) : IChatUpdate;
