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

    public string? DefaultProviderId { get; set; }

    /// <summary>Distinguishes an intentionally empty provider list from legacy configuration.</summary>
    public bool ProvidersConfigured { get; set; }

    public Dictionary<string, AIProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool UsesNamedProviders => ProvidersConfigured || Providers.Count > 0;
}
