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

    internal bool AllowAnonymous { get; set; }

    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    ///     Wire protocol used by this endpoint. Endpoints that implement Chat Completions must
    ///     select that protocol explicitly.
    /// </summary>
    public OpenAIApiMode ApiMode { get; set; } = OpenAIApiMode.Responses;

    [Range(1, int.MaxValue)]
    public int MaxTokens { get; set; } = 1024;
}
