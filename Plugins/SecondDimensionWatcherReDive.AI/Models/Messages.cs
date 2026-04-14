using SecondDimensionWatcherReDive.AI.Abstractions;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record SystemMessage(string Content) : IMessage
{
    public MessageRole Role => MessageRole.System;
}

public sealed record UserMessage(string Content) : IMessage
{
    public MessageRole Role => MessageRole.User;
}

public sealed record AssistantMessage(string? Content, IReadOnlyList<ToolCall>? ToolCalls = null) : IMessage
{
    public MessageRole Role => MessageRole.Assistant;
}

public sealed record ToolResultMessage(string ToolCallId, string Content) : IMessage
{
    public MessageRole Role => MessageRole.Tool;
}
