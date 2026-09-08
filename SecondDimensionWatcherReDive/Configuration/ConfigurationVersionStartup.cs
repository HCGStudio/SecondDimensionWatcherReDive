using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using SecondDimensionWatcherReDive.ConfigMigration;

namespace SecondDimensionWatcherReDive.Configuration;

internal static class ConfigurationVersionStartup
{
    private const string EnvironmentSchemaVersion = "SDW_CONFIG_VERSION";

    internal static async Task PrepareAsync(WebApplicationBuilder builder, CancellationToken cancellationToken)
    {
        var configuration = builder.Configuration;
        var runner = new ConfigMigrationRunner();
        var workingDirectory = Directory.GetCurrentDirectory();
        var contentRoot = builder.Environment.ContentRootPath;
        var legacyPasswordFile = ResolveLegacyPasswordFile(configuration, contentRoot);
        var inherited = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var inheritedFiles = new Dictionary<string, byte[]>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var sawApplicationFile = false;
        // Process original sources in priority order. An overlay's own Version determines its
        // migration; lower settings provide defaults without promoting them into that overlay.
        foreach (var source in configuration.Sources.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source is EnvironmentVariablesConfigurationSource { Prefix: { Length: > 0 } }) continue;
            var provider = source.Build(configuration);
            try
            {
                provider.Load();
                var readApplicationFile = false;
                if (source is JsonConfigurationSource { Path: { } sourcePath } json
                    && Path.GetFileName(sourcePath).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                {
                    var isOverlay = sawApplicationFile;
                    sawApplicationFile = true;
                    var path = json.FileProvider?.GetFileInfo(sourcePath).PhysicalPath
                               ?? Path.GetFullPath(sourcePath, builder.Environment.ContentRootPath);
                    if (!File.Exists(path) && json.Optional)
                    {
                        if (ReadProvider(provider).Any())
                            throw new ConfigMigrationException($"Configuration source '{path}' disappeared during migration. Retry with the current sources.");
                        continue;
                    }
                    var result = await runner.MigrateFileAsync(path, workingDirectory, null,
                        cancellationToken, new ConfigMigrationOptions(inherited, isOverlay, contentRoot, legacyPasswordFile,
                            InheritedConfigurationSnapshots: inheritedFiles));
                    Report(path, result);
                    readApplicationFile = true;
                }

                var values = ReadApplicationProvider(source, provider).ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                (string Path, byte[] Content)? fileSnapshot = null;
                if (source is JsonConfigurationSource { Path: { } filePath } file)
                {
                    var path = file.FileProvider?.GetFileInfo(filePath).PhysicalPath
                               ?? Path.GetFullPath(filePath, contentRoot);
                    if (File.Exists(path))
                    {
                        path = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
                        var content = await File.ReadAllBytesAsync(path, cancellationToken);
                        // Parse the same bytes retained for the commit-time check.
                        // A separate provider.Load followed by a file read can race a replacement.
                        using var stream = new MemoryStream(content, writable: false);
                        var snapshot = new ConfigurationBuilder().AddJsonStream(stream).Build();
                        using var lifetime = snapshot as IDisposable;
                        values = ReadProvider(snapshot.Providers.Single()).ToDictionary(
                            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                        fileSnapshot = (path, content);
                    }
                    else if (readApplicationFile || values.Count > 0 || !file.Optional)
                        throw new ConfigMigrationException($"Configuration source '{path}' disappeared during migration. Retry with the current sources.");
                }
                IReadOnlyDictionary<string, string?> migrated = values;
                if (source is EnvironmentVariablesConfigurationSource { Prefix: null or "" } or CommandLineConfigurationSource
                    || source is JsonConfigurationSource { Path: { } jsonPath }
                    && !Path.GetFileName(jsonPath).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                {
                    // The environment uses SDW_CONFIG_VERSION for schema metadata;
                    // generic VERSION and stripped hosting versions are unrelated.
                    // User secrets, environment and command-line settings receive only the
                    // changed keys at their own priority, without rewriting their source.
                    if (ConfigMigrationRunner.ContainsMigrationSettings(values))
                    {
                        migrated = await runner.MigrateSettingsAsync(values, workingDirectory,
                            cancellationToken, new ConfigMigrationOptions(inherited, IsOverlay: true,
                                ContentRootDirectory: contentRoot, LegacyPasswordFile: legacyPasswordFile));
                        var changes = ChangedValues(values, migrated);
                        if (changes.Length > 0)
                            configuration.Sources.Insert(configuration.Sources.IndexOf(source) + 1,
                                new MemoryConfigurationSource { InitialData = changes });
                    }
                }

                foreach (var pair in migrated) inherited[pair.Key] = pair.Value;
                if (fileSnapshot is { } loaded) inheritedFiles[loaded.Path] = loaded.Content;
            }
            finally
            {
                // Dispose the temporary file-watcher registration. Chained/custom providers
                // can share the host configuration, whose lifetime remains with the builder.
                if (source is JsonConfigurationSource)
                    (provider as IDisposable)?.Dispose();
            }
        }
        ((IConfigurationRoot)configuration).Reload();

        if (inherited.GetValueOrDefault("Config") is { } configPath)
        {
            var path = Path.GetFullPath(configPath, builder.Environment.ContentRootPath);
            var result = await runner.MigrateFileAsync(path, workingDirectory, null,
                cancellationToken, new ConfigMigrationOptions(inherited,
                    ContentRootDirectory: contentRoot, LegacyPasswordFile: legacyPasswordFile,
                    InheritedConfigurationSnapshots: inheritedFiles));
            Report(path, result);
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                configuration.AddJsonFile(path, optional: false, reloadOnChange: true);
            else
                configuration.AddYamlFile(path, optional: false, reloadOnChange: true);
        }

        var current = ReadApplicationConfiguration(configuration);
        var effective = await runner.MigrateSettingsAsync(current, workingDirectory, cancellationToken,
            new ConfigMigrationOptions(ContentRootDirectory: contentRoot, LegacyPasswordFile: legacyPasswordFile));
        var finalChanges = ChangedValues(current, effective).ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        // Expose the actual schema version to the application even when an
        // unrelated VERSION environment variable shadows the file provider.
        finalChanges["Version"] = effective["Version"];
        configuration.AddInMemoryCollection(finalChanges);
        LoadBootstrapCredentials(configuration, contentRoot);
    }

    private static Dictionary<string, string?> ReadApplicationConfiguration(ConfigurationManager configuration)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        // Keep the live hosting providers intact, but do not reinterpret stripped
        // DOTNET_/ASPNETCORE_ keys as application schema or inherited settings.
        // Chained hosting settings remain in order; the default web-host chain
        // suppresses its own environment provider and carries in-memory settings.
        foreach (var (source, provider) in configuration.Sources.Zip(((IConfigurationRoot)configuration).Providers))
        {
            if (source is EnvironmentVariablesConfigurationSource { Prefix: { Length: > 0 } }) continue;
            foreach (var pair in ReadApplicationProvider(source, provider)) values[pair.Key] = pair.Value;
        }
        return values;
    }

    private static string ResolveLegacyPasswordFile(ConfigurationManager configuration, string contentRoot)
    {
        var passwordFile = "password.json";
        // These keys name the same credential authority across schema versions.
        // Follow source priority so a higher legacy PasswordFile can supersede a
        // lower canonical reference, while a reference wins within its own source.
        foreach (var (source, provider) in configuration.Sources.Zip(((IConfigurationRoot)configuration).Providers))
        {
            if (source is EnvironmentVariablesConfigurationSource { Prefix: { Length: > 0 } }) continue;
            ApplyProvider(provider);
        }
        if (ReadApplicationConfiguration(configuration).GetValueOrDefault("Config") is not { } configPath) return passwordFile;
        // Before migration removes PasswordFile from individual layers, resolve
        // the value the old host selected after appending its external document.
        var path = Path.GetFullPath(configPath, contentRoot);
        var external = new ConfigurationBuilder().SetBasePath(contentRoot);
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            external.AddJsonFile(path, optional: false);
        else
            external.AddYamlFile(path, optional: false);
        var document = external.Build();
        using var lifetime = document as IDisposable;
        foreach (var provider in document.Providers) ApplyProvider(provider);
        return passwordFile;

        void ApplyProvider(IConfigurationProvider provider)
        {
            if (provider.TryGet("PasswordFile", out var value)) passwordFile = value ?? "password.json";
            if (provider.TryGet("Authentication:BootstrapCredentialsFile", out var credentials)
                && !string.IsNullOrWhiteSpace(credentials)) passwordFile = credentials;
        }
    }

    private static void LoadBootstrapCredentials(ConfigurationManager configuration, string contentRoot)
    {
        if (configuration["Authentication:BootstrapCredentialsFile"] is not { } credentialsPath
            || string.IsNullOrWhiteSpace(credentialsPath)) return;

        var path = Path.GetFullPath(credentialsPath, contentRoot);
        try
        {
            var credentials = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
            using var lifetime = credentials as IDisposable;
            var hash = credentials["Authentication:BootstrapPasswordHash"] ?? credentials["Password:Value"];
            if (string.IsNullOrWhiteSpace(hash))
                throw new ConfigMigrationException("The bootstrap credentials file does not contain a password hash.");
            // This protected file was the final credential authority in legacy
            // deployments. Import only its hash, and only into the in-memory layer.
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:BootstrapPasswordHash"] = hash
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            throw new ConfigMigrationException($"Cannot read bootstrap credentials file '{path}'. Check its JSON format and access permissions.");
        }
    }

    private static KeyValuePair<string, string?>[] ChangedValues(
        IReadOnlyDictionary<string, string?> original,
        IReadOnlyDictionary<string, string?> migrated) =>
        migrated.Where(pair => !original.TryGetValue(pair.Key, out var value) || value != pair.Value).ToArray();

    private static IEnumerable<KeyValuePair<string, string?>> ReadApplicationProvider(
        IConfigurationSource source, IConfigurationProvider provider)
    {
        if (source is not EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            return ReadProvider(provider);

        return ReadProvider(provider)
            .Where(pair => !pair.Key.Equals("Version", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key.Equals(EnvironmentSchemaVersion, StringComparison.OrdinalIgnoreCase)
                ? new KeyValuePair<string, string?>("Version", pair.Value) : pair);
    }

    private static IEnumerable<KeyValuePair<string, string?>> ReadProvider(IConfigurationProvider provider, string? parent = null)
    {
        foreach (var key in provider.GetChildKeys([], parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = parent is null ? key : $"{parent}:{key}";
            if (provider.TryGet(path, out var value)) yield return new(path, value);
            foreach (var pair in ReadProvider(provider, path)) yield return pair;
        }
    }

    private static void Report(string path, ConfigMigrationResult result)
    {
        if (result.AppliedMigrations.Count > 0)
            Console.Error.WriteLine($"Configuration '{path}' upgraded from {result.FromVersion} to {result.ToVersion}. Backup: {result.BackupPath}");
    }
}
