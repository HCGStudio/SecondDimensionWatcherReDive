namespace SecondDimensionWatcherReDive.Inference.AI.Configuration;

public class InferenceOptions
{
    public const string SectionName = "Inference";

    /// <summary>"OpenAI" or "Anthropic"</summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>Base URL for the API endpoint (e.g., "https://api.openai.com/v1" or any compatible endpoint)</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API key for authentication</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model name (e.g., "gpt-4o", "claude-sonnet-4-20250514")</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Maximum tokens for the response</summary>
    public int MaxTokens { get; set; } = 1024;
}
