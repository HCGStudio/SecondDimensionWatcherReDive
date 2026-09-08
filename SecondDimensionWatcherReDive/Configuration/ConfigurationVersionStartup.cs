using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using SecondDimensionWatcherReDive.ConfigMigration;

namespace SecondDimensionWatcherReDive.Configuration;

internal static class ConfigurationVersionStartup
{
    internal static async Task PrepareAsync(WebApplicationBuilder builder, CancellationToken cancellationToken)
    {
        var configuration = builder.Configuration;
        var runner = new ConfigMigrationRunner();
        var workingDirectory = Directory.GetCurrentDirectory();
        var contentRoot = builder.Environment.ContentRootPath;
        var legacyPasswordFile = ResolveLegacyPasswordFile(configuration, contentRoot);
        var inherited = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var sawApplicationFile = false;
        // Process original sources in priority order. An overlay's own Version determines its
        // migration; lower settings provide defaults without promoting them into that overlay.
        foreach (var source in configuration.Sources.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = source.Build(configuration);
            try
            {
                provider.Load();
                if (source is JsonConfigurationSource { Path: { } sourcePath } json
                    && Path.GetFileName(sourcePath).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                {
                    var isOverlay = sawApplicationFile;
                    sawApplicationFile = true;
                    var path = json.FileProvider?.GetFileInfo(sourcePath).PhysicalPath
                               ?? Path.GetFullPath(sourcePath, builder.Environment.ContentRootPath);
                    if (!File.Exists(path) && json.Optional) continue;
                    var result = await runner.MigrateFileAsync(path, workingDirectory, null,
                        cancellationToken, new ConfigMigrationOptions(inherited, isOverlay, contentRoot, legacyPasswordFile));
                    Report(path, result);
                    provider.Load();
                }

                var values = ReadProvider(provider).ToDictionary(
                    pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
                IReadOnlyDictionary<string, string?> migrated = values;
                if (source is EnvironmentVariablesConfigurationSource { Prefix: null or "" } or CommandLineConfigurationSource
                    || source is JsonConfigurationSource { Path: { } jsonPath }
                    && !Path.GetFileName(jsonPath).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                {
                    // Only the unprefixed application environment participates. Host sources
                    // strip DOTNET_/ASPNETCORE_ and can expose runtime versions as "Version".
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
                    ContentRootDirectory: contentRoot, LegacyPasswordFile: legacyPasswordFile));
            Report(path, result);
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                configuration.AddJsonFile(path, optional: false, reloadOnChange: true);
            else
                configuration.AddYamlFile(path, optional: false, reloadOnChange: true);
        }

        var current = configuration.AsEnumerable().ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var effective = await runner.MigrateSettingsAsync(current, workingDirectory, cancellationToken,
            new ConfigMigrationOptions(ContentRootDirectory: contentRoot, LegacyPasswordFile: legacyPasswordFile));
        var finalChanges = ChangedValues(current, effective);
        if (finalChanges.Length > 0)
            configuration.AddInMemoryCollection(finalChanges);
    }

    private static string ResolveLegacyPasswordFile(IConfiguration configuration, string contentRoot)
    {
        var passwordFile = configuration["PasswordFile"] ?? "password.json";
        if (configuration["Config"] is not { } configPath) return passwordFile;
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
        foreach (var provider in document.Providers.Reverse())
            if (provider.TryGet("PasswordFile", out var value)) return value ?? "password.json";
        return passwordFile;
    }

    private static KeyValuePair<string, string?>[] ChangedValues(
        IReadOnlyDictionary<string, string?> original,
        IReadOnlyDictionary<string, string?> migrated) =>
        migrated.Where(pair => !original.TryGetValue(pair.Key, out var value) || value != pair.Value).ToArray();

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
