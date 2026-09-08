using System.Text.Json.Nodes;
using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration;

public sealed class ConfigMigrationRunner
{
    public static Version BaselineVersion { get; } = new(2, 2, 0);
    private static readonly HashSet<string> MigrationRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "Version", "AI", "Inference", "Password", "PasswordFile", "StateDirectory", "Authentication"
    };

    public static bool ContainsMigrationSettings(IEnumerable<KeyValuePair<string, string?>> settings) =>
        settings.Any(pair => MigrationRoots.Contains(pair.Key.Split(':')[0]));
    private readonly IReadOnlyList<IConfigMigration> migrations;
    public Version CurrentVersion { get; }

    public ConfigMigrationRunner()
    {
        migrations = GeneratedConfigMigrations.Create().OrderBy(step => step.Definition.FromVersion).ToArray();
        var version = BaselineVersion;
        foreach (var migration in migrations)
        {
            if (migration.Definition.FromVersion != version || migration.Definition.ToVersion <= version)
                throw new ConfigMigrationException("The generated configuration migration chain has a gap, duplicate or invalid version.");
            version = migration.Definition.ToVersion;
        }
        CurrentVersion = version;
    }

    public async Task<ConfigMigrationResult> MigrateFileAsync(
        string path, string workingDirectory,
        Func<ConfigMigrationChoice, CancellationToken, Task<string>>? chooseAsync,
        CancellationToken cancellationToken, ConfigMigrationOptions? options = null)
    {
        path = Path.GetFullPath(path);
        var formatPath = path;
        // Follow the target so an atomically replaced configuration keeps its public symlink.
        if (new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true) is { } target) path = target.FullName;
        try
        {
            var original = await File.ReadAllBytesAsync(path, cancellationToken);
            var document = ConfigFileFormat.Read(System.Text.Encoding.UTF8.GetString(original).TrimStart('\uFEFF'), formatPath);
            var (updated, result) = await MigrateAsync(document, workingDirectory, chooseAsync, cancellationToken, options);
            if (result.AppliedMigrations.Count == 0) return result;
            var backup = await ConfigFileWriter.ReplaceAsync(path, original,
                ConfigFileFormat.Write(updated, formatPath), cancellationToken);
            return result with { BackupPath = backup };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigMigrationException($"Cannot read or atomically update configuration '{path}'. Check file and directory permissions; run sdw-migrate with the required access.");
        }
    }

    /// <summary>Upgrade the complete effective configuration in memory, including environment overrides.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> MigrateSettingsAsync(
        IEnumerable<KeyValuePair<string, string?>> settings, string workingDirectory,
        CancellationToken cancellationToken, ConfigMigrationOptions? options = null)
    {
        var document = ConfigTree.Object();
        var entries = settings.ToArray();
        foreach (var pair in entries.Where(pair => pair.Value is not null).OrderBy(pair => pair.Key.Count(c => c == ':')))
            ConfigTree.Set(document, pair.Key, JsonValue.Create(pair.Value));
        var (updated, _) = await MigrateAsync(document, workingDirectory, null, cancellationToken, options);
        var result = entries.ToDictionary(pair => pair.Key, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ConfigTree.Flatten(updated)) result[pair.Key] = pair.Value;
        return result;
    }

    private async Task<(JsonObject Configuration, ConfigMigrationResult Result)> MigrateAsync(
        JsonObject document, string workingDirectory,
        Func<ConfigMigrationChoice, CancellationToken, Task<string>>? chooseAsync,
        CancellationToken cancellationToken, ConfigMigrationOptions? options)
    {
        var versionText = ConfigTree.Text(document, "Version");
        var version = versionText is null ? BaselineVersion : ParseVersion(versionText);
        if (version > CurrentVersion)
            throw new ConfigMigrationException($"Configuration version {version} is newer than supported version {CurrentVersion}.");
        if (version < BaselineVersion)
            throw new ConfigMigrationException($"Configuration version {version} predates the supported baseline {BaselineVersion}.");
        var originalVersion = version;
        var updated = (JsonObject)document.DeepClone();
        var context = new ConfigMigrationContext(updated, Path.GetFullPath(workingDirectory), options?.InheritedSettings, options?.IsOverlay ?? false);
        var applied = new List<ConfigMigrationDefinition>();
        while (version < CurrentVersion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = migrations.SingleOrDefault(step => step.Definition.FromVersion == version)
                       ?? throw new ConfigMigrationException($"No configuration migration starts at {version}.");
            var choices = step.GetRequiredChoices(context);
            if (choices.Count > 0 && !step.Definition.MayRequireUserIntervention)
                throw new ConfigMigrationException("A configuration migration requested undeclared user intervention.");
            if (choices.Count > 0 && chooseAsync is null)
                throw new ConfigMigrationException($"Configuration migration {version} → {step.Definition.ToVersion} requires user intervention. Run sdw-migrate --config <path> in a terminal. Required choices: {string.Join(", ", choices.Select(choice => choice.Key))}.");
            var selections = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var choice in choices)
            {
                var value = await chooseAsync!(choice, cancellationToken);
                if (!choice.Options.Any(option => option.Value == value))
                    throw new ConfigMigrationException($"Invalid selection for '{choice.Key}'.");
                selections.Add(choice.Key, value);
            }
            step.Up(context, selections);
            version = step.Definition.ToVersion;
            ConfigTree.Set(updated, "Version", JsonValue.Create(version.ToString(3)));
            applied.Add(step.Definition);
        }
        ValidateCurrent(updated);
        return (updated, new(originalVersion, version, applied, null));
    }

    private static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new ConfigMigrationException("Configuration Version must be a three-part version such as 2.3.0.");
        return version;
    }

    private static void ValidateCurrent(JsonObject config)
    {
        var obsolete = Migrations.MoveInferenceToAi.LegacyKeys.Select(key => $"Inference:{key}")
            .Concat(["PasswordFile", "Password"])
            .Where(path => ConfigTree.Get(config, path) is not null).ToArray();
        if (obsolete.Length > 0)
            throw new ConfigMigrationException($"Current-version configuration contains obsolete fields: {string.Join(", ", obsolete)}. Correct its Version before running sdw-migrate.");
    }
}
