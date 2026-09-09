namespace SecondDimensionWatcherReDive.AI.Configuration;

public enum AIProviderProtocol
{
    OpenAIResponses,
    OpenAIChatCompletions,
    Anthropic,
    CodexAppServer
}

/// <summary>A named endpoint; its dictionary key is its stable provider identity.</summary>
public sealed class AIProviderOptions
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AIProviderProtocol Protocol { get; set; } = AIProviderProtocol.OpenAIResponses;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.6-luna";
    public int MaxTokens { get; set; } = 16384;
    public string? ReasoningEffort { get; set; }
    public string ApiVersion { get; set; } = "2023-06-01";
    public List<AIProviderModelOptions> Models { get; set; } = [];
    public string Endpoint { get; set; } = string.Empty;
    public string? BearerToken { get; set; }
    public string PermissionProfile { get; set; } = ":read-only";
    public int TimeoutSeconds { get; set; } = 300;
}

public sealed class AIProviderModelOptions
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }

    /// <summary>Null uses known model capabilities; an empty list explicitly disables effort.</summary>
    public List<string>? ReasoningEfforts { get; set; }

    public bool ReasoningEffortsConfigured { get; set; }
}
