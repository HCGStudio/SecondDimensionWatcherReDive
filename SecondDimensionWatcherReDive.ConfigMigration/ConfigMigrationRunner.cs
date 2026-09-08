using System.Text.Json.Nodes;
using SecondDimensionWatcherReDive.Framework.ConfigurationMigration;

namespace SecondDimensionWatcherReDive.ConfigMigration;

public sealed class ConfigMigrationRunner
{
    public static Version BaselineVersion { get; } = new(2, 2, 0);
    private static readonly HashSet<string> MigrationPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "Version", "PasswordFile", "Password:Value", "StateDirectory", "Authentication:BootstrapPasswordHash", "Authentication:BootstrapCredentialsFile",
        "AI:Engine", "AI:Provider",
        "AI:OpenAI:ApiKey", "AI:OpenAI:BaseUrl", "AI:OpenAI:ApiMode", "AI:OpenAI:Model", "AI:OpenAI:MaxTokens",
        "AI:Anthropic:ApiKey", "AI:Anthropic:BaseUrl", "AI:Anthropic:Model", "AI:Anthropic:MaxTokens", "AI:Anthropic:ApiVersion",
        "AI:CodexAppServer:Endpoint", "AI:CodexAppServer:Model", "AI:CodexAppServer:PermissionProfile",
        "AI:CodexAppServer:TimeoutSeconds", "AI:CodexAppServer:Token",
        "Inference:Provider", "Inference:ApiKey", "Inference:BaseUrl", "Inference:Model", "Inference:MaxTokens",
        "Inference:RateLimitDelayMs"
    };
    private static readonly HashSet<string> MigrationSections = new(StringComparer.OrdinalIgnoreCase)
        { "AI", "Inference", "Password", "Authentication" };

    public static bool ContainsMigrationSettings(IEnumerable<KeyValuePair<string, string?>> settings) =>
        settings.Any(pair => MigrationPaths.Contains(pair.Key));
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
            var (effectiveOptions, inheritedSources) = await ResolveFileInheritanceAsync(
                document, path, workingDirectory, options, cancellationToken);
            var (updated, result) = await MigrateAsync(document, workingDirectory, chooseAsync, cancellationToken, effectiveOptions);
            if (result.AppliedMigrations.Count == 0) return result;
            using var migrationLocks = ConfigFileWriter.AcquireMigrationLocks(path, inheritedSources.Select(source => source.Path));
            foreach (var source in inheritedSources)
                if (!(await File.ReadAllBytesAsync(source.Path, cancellationToken)).AsSpan().SequenceEqual(source.Content))
                    throw new ConfigMigrationException("Inherited configuration changed during migration. No migration was written; retry with the current context.");
            var backup = await ConfigFileWriter.ReplaceAsync(path, original,
                ConfigFileFormat.Write(updated, formatPath), cancellationToken);
            return result with { BackupPath = backup };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ConfigMigrationException($"Cannot read or atomically update configuration '{path}'. Check file and directory permissions; run sdw-migrate with the required access.");
        }
    }

    private async Task<(ConfigMigrationOptions? Options, IReadOnlyList<(string Path, byte[] Content)> Sources)>
        ResolveFileInheritanceAsync(JsonObject target, string targetPath, string workingDirectory,
            ConfigMigrationOptions? options, CancellationToken cancellationToken)
    {
        var versionText = ConfigTree.Text(target, "Version");
        var version = versionText is null ? BaselineVersion : ParseVersion(versionText);
        // Current files do not run any Up or write data, so no inheritance declaration
        // is needed for a no-op. Unsupported versions retain their normal diagnostics.
        if (version >= CurrentVersion || version < BaselineVersion) return (options, []);
        var files = options?.InheritedConfigurationFiles ?? [];
        if (options?.InheritedConfigurationSnapshots is { Count: > 0 } snapshots)
        {
            if (files.Count > 0)
                throw new ConfigMigrationException("Supply inherited file paths or their loaded snapshots, not both.");
            // Startup already combined these exact bytes with intervening memory,
            // environment and command-line sources. Re-reading and reconstructing
            // only the files here would change that source ordering or retain stale keys.
            var loadedPaths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                { targetPath };
            var loaded = new List<(string Path, byte[] Content)>();
            foreach (var snapshot in snapshots)
            {
                var path = Path.GetFullPath(snapshot.Key);
                if (!loadedPaths.Add(path))
                    throw new ConfigMigrationException("Inherited configuration snapshots must be distinct and cannot include the target file.");
                loaded.Add((path, snapshot.Value.ToArray()));
            }
            return (options, loaded);
        }
        if (files.Count == 0)
        {
            if (options?.RequireExplicitInheritance == true)
                throw new ConfigMigrationException("Migrating a legacy file requires its complete lower-priority context via --inherit-config, or --standalone when the file has no inherited configuration.");
            return (options, []);
        }

        var sources = new List<(string Path, byte[] Content, JsonObject Document)>();
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            { targetPath };
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(file);
            var formatPath = path;
            if (new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true) is { } linked) path = linked.FullName;
            if (!paths.Add(path))
                throw new ConfigMigrationException("Inherited configuration files must be distinct and cannot include the target file.");
            var content = await File.ReadAllBytesAsync(path, cancellationToken);
            var document = ConfigFileFormat.Read(System.Text.Encoding.UTF8.GetString(content).TrimStart('\uFEFF'), formatPath);
            sources.Add((path, content, document));
        }

        var passwordFile = options?.LegacyPasswordFile ?? "password.json";
        // The old password file followed the entire configuration chain. Select it
        // before migrating any layer, while every original PasswordFile still exists.
        foreach (var document in sources.Select(source => source.Document).Append(target))
            if (document.ContainsKey("PasswordFile"))
                passwordFile = ConfigTree.Text(document, "PasswordFile") ?? "password.json";

        var inherited = options?.InheritedSettings?.ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            JsonObject updated;
            try
            {
                (updated, _) = await MigrateAsync(source.Document, workingDirectory, null, cancellationToken,
                    new ConfigMigrationOptions(inherited, IsOverlay: inherited.Count > 0,
                        ContentRootDirectory: options?.ContentRootDirectory, LegacyPasswordFile: passwordFile));
            }
            catch (ConfigMigrationException exception)
            {
                throw new ConfigMigrationException($"Inherited configuration '{source.Path}' cannot be migrated silently. Migrate that layer with its own context first. {exception.Message}");
            }
            foreach (var pair in ConfigTree.Flatten(source.Document)) inherited[pair.Key] = null;
            foreach (var pair in ConfigTree.Flatten(updated)) inherited[pair.Key] = pair.Value;
        }
        return ((options ?? new ConfigMigrationOptions()) with
        {
            InheritedSettings = inherited,
            IsOverlay = true,
            LegacyPasswordFile = passwordFile
        }, sources.Select(source => (source.Path, source.Content)).ToArray());
    }

    /// <summary>Upgrade the complete effective configuration in memory, including environment overrides.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> MigrateSettingsAsync(
        IEnumerable<KeyValuePair<string, string?>> settings, string workingDirectory,
        CancellationToken cancellationToken, ConfigMigrationOptions? options = null)
    {
        var document = ConfigTree.Object();
        // IConfiguration can contain an unrelated scalar such as PASSWORD or AI
        // alongside real section descendants. It is not an application setting;
        // leave it in the original provider rather than migrate or clear it.
        var entries = settings.Where(pair => !MigrationSections.Contains(pair.Key)).ToArray();
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
        var originalDirectory = Path.GetFullPath(workingDirectory);
        var context = new ConfigMigrationContext(updated, originalDirectory, options?.InheritedSettings,
            options?.IsOverlay ?? false, options?.ContentRootDirectory is { } contentRoot
                ? Path.GetFullPath(contentRoot, originalDirectory) : null, options?.LegacyPasswordFile);
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
