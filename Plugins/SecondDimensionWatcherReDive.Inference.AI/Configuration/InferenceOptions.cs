using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Inference.AI.Configuration;

public class InferenceOptions
{
    public const string SectionName = "Inference";

    public string? ProviderId { get; set; }

    public string? Model { get; set; }

    public string? ReasoningEffort { get; set; }

    /// <summary>Minimum interval in milliseconds between API calls to avoid rate limiting (default: 1000ms)</summary>
    [Range(0, int.MaxValue)]
    public int RateLimitDelayMs { get; set; } = 1000;
}
