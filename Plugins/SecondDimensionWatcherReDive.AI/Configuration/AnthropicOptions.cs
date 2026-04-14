using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.AI.Configuration;

public sealed class AnthropicOptions
{
    public const string SectionName = "AI:Anthropic";

    [Required, Url]
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Model { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int MaxTokens { get; set; } = 1024;

    [Required]
    public string ApiVersion { get; set; } = "2023-06-01";
}
