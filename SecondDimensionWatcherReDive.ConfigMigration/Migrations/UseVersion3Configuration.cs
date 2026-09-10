using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration.Migrations;

internal sealed class UseVersion3Configuration : IConfigMigration
{
    public ConfigMigrationDefinition Definition { get; } = new(
        new Version(2, 3, 0), new Version(3, 0, 0),
        "Advance the configuration version to 3.0.0 without changing settings.", MayRequireUserIntervention: false);

    public IReadOnlyList<ConfigMigrationChoice> GetRequiredChoices(ConfigMigrationContext context) => [];

    public void Up(ConfigMigrationContext context, IReadOnlyDictionary<string, string> selections)
    {
        // The existing settings remain valid; the runner updates Version for this step.
    }
}
