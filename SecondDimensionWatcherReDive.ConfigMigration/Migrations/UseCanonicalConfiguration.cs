using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration.Migrations;

internal sealed class UseCanonicalConfiguration : IConfigMigration
{
    private readonly ConditionalWeakTable<ConfigMigrationContext, LegacyCredential> credentials = new();
    private sealed record LegacyCredential(string? Hash);
    private const string PasswordKey = "Authentication:BootstrapPasswordHash";
    public ConfigMigrationDefinition Definition { get; } = new(
        new Version(2, 2, 1), new Version(2, 3, 0),
        "Use explicit AI protocol, state directory and bootstrap credentials.", MayRequireUserIntervention: true);

    public IReadOnlyList<ConfigMigrationChoice> GetRequiredChoices(ConfigMigrationContext context)
    {
        var hash = credentials.GetValue(context, static value => new(ReadLegacyHash(value))).Hash;
        var current = ConfigTree.Text(context.Configuration, PasswordKey);
        var choices = new List<ConfigMigrationChoice>();
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(current) && hash != current)
            choices.Add(new(PasswordKey,
                "A legacy login hash differs from Authentication:BootstrapPasswordHash. Choose the bootstrap credential to retain (database credentials stay authoritative).",
                [new("current", "Keep the current bootstrap hash"), new("legacy", "Import the legacy login hash")]));
        if (HasStateConflict(context, hash))
            choices.Add(new("StateDirectory",
                "StateDirectory differs from the legacy password directory. Moving implicit state paths can lose access to encryption keys, plugins and cached media.",
                [new("legacy", "Keep implicit state in the legacy directory"), new("current", "Use StateDirectory (move the existing state there before restarting)")]));
        return choices;
    }

    public void Up(ConfigMigrationContext context, IReadOnlyDictionary<string, string> selections)
    {
        var config = context.Configuration;
        var hash = credentials.GetValue(context, static value => new(ReadLegacyHash(value))).Hash;
        if (!string.IsNullOrWhiteSpace(hash) && (string.IsNullOrWhiteSpace(ConfigTree.Text(config, PasswordKey))
                                              || selections.GetValueOrDefault(PasswordKey) == "legacy"))
            ConfigTree.Set(config, PasswordKey, JsonValue.Create(hash));
        var passwordPath = PasswordPath(context);
        if (selections.GetValueOrDefault("StateDirectory") == "legacy")
        {
            var directory = Path.GetDirectoryName(passwordPath)!;
            foreach (var (key, subdirectory) in new[]
                     { ("DataProtection:KeyRingPath", "data-protection-keys"), ("PluginPlatform:RootPath", "plugins"), ("Transcoding:CachePath", "transcode-cache") })
                if (string.IsNullOrWhiteSpace(ConfigTree.Text(config, key))
                    && string.IsNullOrWhiteSpace(context.InheritedSettings?.GetValueOrDefault(key)))
                    ConfigTree.Set(config, key, JsonValue.Create(Path.Combine(directory, subdirectory)));
        }
        if (ConfigTree.Get(config, "StateDirectory") is null
            && (ConfigTree.Get(config, "PasswordFile") is not null || !string.IsNullOrWhiteSpace(hash))
            && (ConfigTree.Get(config, "PasswordFile") is not null || context.InheritedSettings?.GetValueOrDefault("StateDirectory") is null))
            ConfigTree.Set(config, "StateDirectory", JsonValue.Create(Path.GetDirectoryName(passwordPath)!));
        // Preserve the old default on migrated deployments. Newly created 2.3 configs
        // use Responses; no unrelated feature is enabled or offered during an upgrade.
        var provider = ConfigTree.Text(config, "AI:Provider")
                       ?? context.InheritedSettings?.GetValueOrDefault("AI:Provider") ?? "OpenAI";
        if ((ConfigTree.Get(config, "AI:OpenAI") is not null || provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            && ConfigTree.Get(config, "AI:OpenAI:ApiMode") is null
            && context.InheritedSettings?.GetValueOrDefault("AI:OpenAI:ApiMode") is null)
            ConfigTree.Set(config, "AI:OpenAI:ApiMode", JsonValue.Create("ChatCompletions"));
        ConfigTree.Remove(config, "Password");
        ConfigTree.Remove(config, "PasswordFile");
    }

    private static string PasswordFile(ConfigMigrationContext context) =>
        context.LegacyPasswordFile ?? ConfigTree.Text(context.Configuration, "PasswordFile") ?? "password.json";

    // The old state defaults used Path.GetFullPath from the process cwd, while
    // AddJsonFile used the content-root provider to locate credential contents.
    private static string PasswordPath(ConfigMigrationContext context) =>
        Path.GetFullPath(PasswordFile(context), context.WorkingDirectory);

    private static string? ReadLegacyHash(ConfigMigrationContext context)
    {
        var hash = ConfigTree.Text(context.Configuration, "Password:Value");
        // The file was appended after every default provider and the external Config.
        // Reapply that final authority in every migrated legacy layer, including
        // when an earlier layer already retained the same bootstrap credential.
        var path = Path.GetFullPath(PasswordFile(context), context.ContentRootDirectory ?? context.WorkingDirectory);
        try
        {
            var password = ConfigFileFormat.Read(File.ReadAllText(path), "password.json");
            // The old password file was the last provider and overrode inline credentials.
            hash = ConfigTree.Text(password, "Password:Value") ?? hash;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        return hash;
    }

    private static bool HasStateConflict(ConfigMigrationContext context, string? legacyHash)
    {
        if (context.IsOverlay && ConfigTree.Get(context.Configuration, "PasswordFile") is null
            && (!string.IsNullOrWhiteSpace(context.InheritedSettings?.GetValueOrDefault(PasswordKey))
                || string.IsNullOrWhiteSpace(legacyHash))) return false;
        var current = ConfigTree.Text(context.Configuration, "StateDirectory");
        if (string.IsNullOrWhiteSpace(current)) return false;
        var legacyDirectory = Path.GetDirectoryName(PasswordPath(context))!;
        if (Path.TrimEndingDirectorySeparator(Path.GetFullPath(current, context.WorkingDirectory)).Equals(
                Path.TrimEndingDirectorySeparator(legacyDirectory),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
        return new[] { "DataProtection:KeyRingPath", "PluginPlatform:RootPath", "Transcoding:CachePath" }
            .Any(key => string.IsNullOrWhiteSpace(ConfigTree.Text(context.Configuration, key))
                        && string.IsNullOrWhiteSpace(context.InheritedSettings?.GetValueOrDefault(key)));
    }
}
