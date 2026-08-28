namespace SecondDimensionWatcherReDive.AI.Configuration;

public sealed class CodexAppServerOptions
{
    public const string SectionName = "AI:CodexAppServer";

    public string Endpoint { get; set; } = string.Empty;

    public string? BearerToken { get; set; }

    public string? Model { get; set; }

    public string PermissionProfile { get; set; } = ":read-only";

    public int TimeoutSeconds { get; set; } = 300;
}
