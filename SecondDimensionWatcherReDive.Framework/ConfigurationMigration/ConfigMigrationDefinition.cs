namespace SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

/// <summary>
/// Describes one ordered Up step. Every step explicitly declares whether any supported input may
/// need user intervention; GetRequiredChoices determines whether the current input actually does.
/// </summary>
public sealed record ConfigMigrationDefinition(
    Version FromVersion,
    Version ToVersion,
    string Description,
    bool MayRequireUserIntervention);
