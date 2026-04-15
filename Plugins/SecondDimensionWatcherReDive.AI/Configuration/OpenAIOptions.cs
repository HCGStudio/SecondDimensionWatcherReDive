using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.AI.Configuration;

public sealed class OpenAIOptions
{
    public const string SectionName = "AI:OpenAI";

    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int MaxTokens { get; set; } = 1024;
}
