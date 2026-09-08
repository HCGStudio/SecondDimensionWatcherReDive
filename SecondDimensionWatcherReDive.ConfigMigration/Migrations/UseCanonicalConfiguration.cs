using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration.Migrations;

internal sealed class UseCanonicalConfiguration : IConfigMigration
{
    private readonly ConditionalWeakTable<ConfigMigrationContext, LegacyCredential> credentials = new();
    private sealed record LegacyCredential(string? Hash, string? FilePath);
    private const string PasswordKey = "Authentication:BootstrapPasswordHash";
    private const string CredentialsFileKey = "Authentication:BootstrapCredentialsFile";
    public ConfigMigrationDefinition Definition { get; } = new(
        new Version(2, 2, 1), new Version(2, 3, 0),
        "Use explicit AI protocol, state directory and bootstrap credentials.", MayRequireUserIntervention: true);

    public IReadOnlyList<ConfigMigrationChoice> GetRequiredChoices(ConfigMigrationContext context)
    {
        var hash = credentials.GetValue(context, ReadLegacyCredential).Hash;
        var current = ConfigTree.Text(context.Configuration, PasswordKey);
        var choices = new List<ConfigMigrationChoice>();
        if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(current) && hash != current)
            choices.Add(new(PasswordKey,
                "A legacy login hash differs from Authentication:BootstrapPasswordHash. Choose the bootstrap credential to retain (database credentials stay authoritative).",
                [new("current", "Keep the current bootstrap hash"), new("legacy", "Import the legacy login hash")]));
        if (HasStateConflict(context))
            choices.Add(new("StateDirectory",
                "StateDirectory differs from the legacy password directory. Moving implicit state paths can lose access to encryption keys, plugins and cached media.",
                [new("legacy", "Keep implicit state in the legacy directory"), new("current", "Use StateDirectory (move the existing state there before restarting)")]));
        return choices;
    }

    public void Up(ConfigMigrationContext context, IReadOnlyDictionary<string, string> selections)
    {
        var config = context.Configuration;
        var credential = credentials.GetValue(context, ReadLegacyCredential);
        var hash = credential.Hash;
        if (selections.GetValueOrDefault(PasswordKey) == "current")
        {
            // An empty value also hides a file reference from lower-priority layers.
            ConfigTree.Set(config, CredentialsFileKey, JsonValue.Create(""));
        }
        else if (!string.IsNullOrWhiteSpace(hash))
        {
            if (credential.FilePath is { } file)
            {
                // Keep protected credentials in their original file. Appsettings may
                // be world-readable or tracked in source control, so persist only a path.
                ConfigTree.Set(config, CredentialsFileKey, JsonValue.Create(file));
                ConfigTree.Remove(config, PasswordKey);
            }
            else
            {
                ConfigTree.Set(config, PasswordKey, JsonValue.Create(hash));
                ConfigTree.Set(config, CredentialsFileKey, JsonValue.Create(""));
            }
        }
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

    private static bool TryGetCredentialsFile(ConfigMigrationContext context, out string? file)
    {
        if (ConfigTree.Get(context.Configuration, "Authentication") is JsonObject authentication
            && authentication.ContainsKey("BootstrapCredentialsFile"))
        {
            file = ConfigTree.Text(context.Configuration, CredentialsFileKey);
            return true;
        }
        file = null;
        // An explicit legacy file in this layer overrides a lower canonical
        // reference. The preselected final legacy path still supplies its authority.
        if (context.Configuration.ContainsKey("PasswordFile")) return false;
        return context.InheritedSettings?.TryGetValue(CredentialsFileKey, out file) == true;
    }

    // The old state defaults used Path.GetFullPath from the process cwd, while
    // AddJsonFile used the content-root provider to locate credential contents.
    private static string PasswordPath(ConfigMigrationContext context)
    {
        // Once the lower layer is canonical, its state directory records the old
        // cwd-based default. The credentials reference instead records a content-
        // root-based path and must not become the state base on later startups.
        if (ConfigTree.Get(context.Configuration, "PasswordFile") is null
            && context.InheritedSettings?.GetValueOrDefault("StateDirectory") is { } inheritedState
            && !string.IsNullOrWhiteSpace(inheritedState))
            return Path.Combine(Path.GetFullPath(inheritedState, context.WorkingDirectory), "password.json");
        return Path.GetFullPath(PasswordFile(context), context.WorkingDirectory);
    }

    private static LegacyCredential ReadLegacyCredential(ConfigMigrationContext context)
    {
        var hash = ConfigTree.Text(context.Configuration, "Password:Value");
        var hasReference = TryGetCredentialsFile(context, out var reference);
        // A previous migration may already have removed PasswordFile from a lower
        // layer. Its canonical reference still carries the final file authority.
        // An explicit empty reference records the user's decision to disable it.
        if (hasReference && string.IsNullOrWhiteSpace(reference)) return new(hash, null);
        // The file was appended after every default provider and the external Config.
        // Reapply that final authority in every migrated legacy layer, including
        // when an earlier layer already retained the same bootstrap credential.
        var path = Path.GetFullPath(hasReference ? reference! : PasswordFile(context),
            context.ContentRootDirectory ?? context.WorkingDirectory);
        try
        {
            var password = ConfigFileFormat.Read(File.ReadAllText(path), "password.json");
            // The old password file was the last provider and overrode inline credentials.
            var fileHash = ConfigTree.Text(password, PasswordKey) ?? ConfigTree.Text(password, "Password:Value");
            if (!string.IsNullOrWhiteSpace(fileHash)) return new(fileHash, path);
            if (hasReference)
                throw new ConfigMigrationException("The bootstrap credentials file does not contain a password hash.");
        }
        catch (FileNotFoundException) when (!hasReference) { }
        catch (DirectoryNotFoundException) when (!hasReference) { }
        return new(hash, null);
    }

    private static bool HasStateConflict(ConfigMigrationContext context)
    {
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
