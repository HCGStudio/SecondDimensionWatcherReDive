namespace SecondDimensionWatcherReDive.AI.Abstractions;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public interface IMessage
{
    MessageRole Role { get; }
}
