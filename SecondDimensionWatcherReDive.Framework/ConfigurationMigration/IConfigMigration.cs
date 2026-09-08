namespace SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

/// <summary>A single forward-only step in the configuration version history.</summary>
public interface IConfigMigration
{
    ConfigMigrationDefinition Definition { get; }

    /// <summary>
    /// Returns only breaking decisions required by this configuration. This method must not
    /// modify the configuration or perform external side effects. An empty list permits a silent Up.
    /// </summary>
    IReadOnlyList<ConfigMigrationChoice> GetRequiredChoices(ConfigMigrationContext context);

    /// <summary>
    /// Applies this step to an in-memory configuration using the required choices collected by the
    /// caller. Do not write files or perform external side effects; the caller commits the complete
    /// migration chain and updates the version. Leave unrelated new features at their defaults.
    /// </summary>
    void Up(ConfigMigrationContext context, IReadOnlyDictionary<string, string> selections);
}
