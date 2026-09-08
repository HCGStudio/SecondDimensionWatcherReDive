namespace SecondDimensionWatcherReDive.ConfigMigration;

/// <summary>Lower-priority values for migrating a configuration overlay without changing inherited defaults.</summary>
public sealed record ConfigMigrationOptions(
    IReadOnlyDictionary<string, string?>? InheritedSettings = null,
    bool IsOverlay = false);
