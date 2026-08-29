namespace SecondDimensionWatcherReDive.AI.Configuration;

public enum AIEngineKind
{
    BuiltIn,
    CodexAppServer
}

public sealed class AIOptions
{
    public const string SectionName = "AI";

    public AIEngineKind Engine { get; set; } = AIEngineKind.BuiltIn;

    public string Provider { get; set; } = "OpenAI";
}
