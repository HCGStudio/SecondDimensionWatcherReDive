using Microsoft.Extensions.Configuration;

namespace SecondDimensionWatcherReDive.Configuration;

public sealed class RuntimeSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly object _gate = new();

    internal RuntimeSettingsConfigurationProvider(IConfiguration deploymentConfiguration)
    {
        DeploymentConfiguration = deploymentConfiguration;
    }

    internal IConfiguration DeploymentConfiguration { get; }

    public override void Load()
    {
    }

    public override bool TryGet(string key, out string? value)
    {
        lock (_gate)
        {
            if (Data.TryGetValue(key, out value))
                return true;
        }

        // This provider owns these runtime-configurable namespaces. Returning an explicit null for
        // unknown keys prevents a lower, reloadable file provider from injecting a new array index
        // (for example an extra MediaLibrary:AllowedRoots entry) after startup validation.
        if (IsRuntimeOwnedKey(key))
        {
            value = null;
            return true;
        }

        value = null;
        return false;
    }

    public override IEnumerable<string> GetChildKeys(
        IEnumerable<string> earlierKeys,
        string? parentPath)
    {
        lock (_gate)
        {
            // ConfigurationRoot folds child keys through providers from lowest to highest. Drop
            // lower-provider children inside an owned subtree so post-startup file reloads cannot
            // grow a validated collection behind the runtime provider.
            var input = IsRuntimeOwnedSection(parentPath) ? [] : earlierKeys;
            return base.GetChildKeys(input, parentPath).ToArray();
        }
    }

    internal void Replace(IReadOnlyDictionary<string, string?> values)
    {
        var replacement = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            Data = replacement;
        }

        OnReload();
    }

    internal IReadOnlyDictionary<string, string?> GetSnapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsRuntimeOwnedKey(string key) =>
        key.Equals(RuntimeSecretKeys.TmdbApiKey, StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("AI:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Inference:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Torrent:Remote:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("MediaLibrary:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Incidents:", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Nfs:", StringComparison.OrdinalIgnoreCase);

    private static bool IsRuntimeOwnedSection(string? path) =>
        path is not null
        && (path.Equals("AI", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("AI:", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Inference", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Inference:", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Torrent:Remote", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Torrent:Remote:", StringComparison.OrdinalIgnoreCase)
            || path.Equals("MediaLibrary", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("MediaLibrary:", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Incidents", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Incidents:", StringComparison.OrdinalIgnoreCase)
            || path.Equals("Nfs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Nfs:", StringComparison.OrdinalIgnoreCase));
}

internal sealed class RuntimeSettingsConfigurationSource(
    RuntimeSettingsConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

public static class RuntimeSettingsConfigurationExtensions
{
    public static RuntimeSettingsConfigurationProvider AddRuntimeSettingsConfigurationProvider(
        this IConfigurationBuilder configurationBuilder)
    {
        IConfiguration deploymentSource;
        IConfigurationRoot? ownedRoot = null;
        if (configurationBuilder is IConfigurationRoot existingRoot)
        {
            deploymentSource = existingRoot;
        }
        else
        {
            var deploymentBuilder = new ConfigurationBuilder();
            foreach (var source in configurationBuilder.Sources)
                deploymentBuilder.Add(source);
            ownedRoot = deploymentBuilder.Build();
            deploymentSource = ownedRoot;
        }

        // Deployment configuration is a startup baseline. Taking a detached snapshot prevents a
        // file-watcher reload from moving a credentialed endpoint while retaining its old secret.
        // Runtime changes must go through the validated, revisioned settings API instead.
        var deploymentSnapshot = deploymentSource.AsEnumerable()
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        (ownedRoot as IDisposable)?.Dispose();
        var deploymentConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(deploymentSnapshot)
            .Build();

        var provider = new RuntimeSettingsConfigurationProvider(deploymentConfiguration);
        configurationBuilder.Add(new RuntimeSettingsConfigurationSource(provider));
        return provider;
    }
}
