namespace SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

/// <summary>One explicit answer to a breaking configuration decision.</summary>
public sealed record ConfigMigrationOption(string Value, string Description);
