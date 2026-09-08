using System.Text.Json.Nodes;

namespace SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

/// <summary>
/// Supplies the in-memory configuration and the original process working directory used to resolve
/// legacy relative paths. A migration may read referenced files but must not modify them.
/// </summary>
/// <param name="Configuration">The current source's document; it does not include lower-priority values.</param>
/// <param name="WorkingDirectory">The original application working directory for resolving relative paths.</param>
/// <param name="InheritedSettings">
/// Already-migrated lower-priority values with flattened, case-insensitive configuration keys.
/// Consult these for inherited defaults without modifying or copying them into the current source.
/// </param>
/// <param name="IsOverlay">
/// Whether this source overrides an earlier configuration source. An overlay must not implicitly
/// reload legacy files or replace inherited settings merely because its own document omits them.
/// </param>
public sealed record ConfigMigrationContext(JsonObject Configuration, string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? InheritedSettings = null,
    bool IsOverlay = false);
