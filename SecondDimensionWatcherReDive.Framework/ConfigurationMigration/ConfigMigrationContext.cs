using System.Text.Json.Nodes;

namespace SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

/// <summary>
/// Supplies the in-memory configuration and the original path bases. A migration may read
/// referenced files but must not modify them.
/// </summary>
/// <param name="Configuration">The current source's document; it does not include lower-priority values.</param>
/// <param name="WorkingDirectory">The original process working directory for relative state paths.</param>
/// <param name="InheritedSettings">
/// Already-migrated lower-priority values with flattened, case-insensitive configuration keys.
/// Consult these for inherited defaults without modifying or copying them into the current source.
/// </param>
/// <param name="IsOverlay">
/// Whether this source overrides an earlier configuration source. An overlay must not replace
/// inherited settings merely because its own document omits them.
/// </param>
/// <param name="ContentRootDirectory">The old host content root for credential files; defaults to WorkingDirectory.</param>
/// <param name="LegacyPasswordFile">The final password-file setting from the old provider chain, when available.</param>
public sealed record ConfigMigrationContext(JsonObject Configuration, string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? InheritedSettings = null,
    bool IsOverlay = false,
    string? ContentRootDirectory = null,
    string? LegacyPasswordFile = null);
