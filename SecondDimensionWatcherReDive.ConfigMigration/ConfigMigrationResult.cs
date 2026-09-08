using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration;

public sealed record ConfigMigrationResult(
    Version FromVersion,
    Version ToVersion,
    IReadOnlyList<ConfigMigrationDefinition> AppliedMigrations,
    string? BackupPath);
