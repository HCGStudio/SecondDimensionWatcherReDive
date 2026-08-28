using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.AI.Configuration;

public enum OpenAIApiMode
{
    /// <summary>OpenAI-compatible <c>POST /chat/completions</c>.</summary>
    ChatCompletions,

    /// <summary>OpenAI <c>POST /responses</c>.</summary>
    Responses
}

public sealed class OpenAIOptions
{
    public const string SectionName = "AI:OpenAI";

    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    ///     Wire protocol used by this endpoint. Chat Completions remains the code default so existing
    ///     Ollama/vLLM/LiteLLM configurations keep working until explicitly migrated.
    /// </summary>
    public OpenAIApiMode ApiMode { get; set; } = OpenAIApiMode.ChatCompletions;

    [Range(1, int.MaxValue)]
    public int MaxTokens { get; set; } = 1024;
}
