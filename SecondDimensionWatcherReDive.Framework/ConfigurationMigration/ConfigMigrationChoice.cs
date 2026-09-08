namespace SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

/// <summary>A required decision about breaking behavior, identified by a stable key.</summary>
public sealed record ConfigMigrationChoice(
    string Key,
    string Description,
    IReadOnlyList<ConfigMigrationOption> Options);
