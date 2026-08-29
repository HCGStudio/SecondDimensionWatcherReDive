using System.Text;
using System.Text.Json;

namespace SecondDimensionWatcherReDive.Framework.Plugin;

public static class PluginApi
{
    public const string CurrentVersion = "1.0";
}

public sealed record PluginManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string ApiVersion { get; init; }
    public required string EntryPoint { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];
    public PluginCapabilities Capabilities { get; init; } = new();
    public IReadOnlyList<string> Platforms { get; init; } = ["any"];
    public PluginIntegrity? Integrity { get; init; }
    public PluginSignature? Signature { get; init; }
    public IReadOnlyList<PluginProviderDeclaration> Providers { get; init; } = [];
    public int DataVersion { get; init; } = 1;
    public PluginDataMigration? DataMigration { get; init; }
}

public sealed record PluginDependency(string Id, string MinimumVersion);

public sealed record PluginCapabilities
{
    public IReadOnlyList<string> NetworkDomains { get; init; } = [];
    public IReadOnlyList<string> FileRoots { get; init; } = [];
    public bool Notifications { get; init; }
    public bool DownloadControl { get; init; }
    public bool StorageAccess { get; init; }
    public bool BackgroundTasks { get; init; }
}

public sealed record PluginIntegrity
{
    /// <summary>
    /// SHA-256 digests for every regular package file except manifest.json. Paths use '/' separators.
    /// The exact path set and every digest are covered by the publisher signature.
    /// </summary>
    public IReadOnlyDictionary<string, string> Files { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record PluginSignature(
    string Publisher,
    string Algorithm,
    string Value);

public sealed record PluginProviderDeclaration
{
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyDictionary<string, string> Handlers { get; init; }
}

public sealed record PluginDataMigration
{
    /// <summary>Preserve or Reset. A data version change requires an explicit Reset.</summary>
    public required string Strategy { get; init; }
    public string? Description { get; init; }
}

public sealed record PluginPackagePreview(
    string Token,
    string PackageSha256,
    PluginManifest Manifest,
    IReadOnlyList<string> CompatibilityErrors,
    bool IsSignatureTrusted,
    string SignatureStatus,
    DateTimeOffset ExpiresAt);

public sealed record PluginInstallResult(
    string Id,
    string Version,
    bool IsUpgrade,
    IReadOnlyList<string> CompatibilityErrors);

public sealed record PluginHealth(
    string Status,
    int ConsecutiveFailures,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    DateTimeOffset? CircuitOpenUntil);

public sealed record InstalledPlugin(
    PluginManifest Manifest,
    bool IsEnabled,
    PluginCapabilities ApprovedCapabilities,
    IReadOnlyList<string> CompatibilityErrors,
    PluginHealth Health,
    JsonElement Configuration,
    bool DataRetainedFromUninstall = false);

/// <summary>
/// Produces the unambiguous payload covered by an RSA-SHA256 publisher signature.
/// Every execution-relevant manifest field is included; the signature value itself is excluded.
/// </summary>
public static class PluginSignaturePayload
{
    public static byte[] Create(PluginManifest manifest)
    {
        var lines = new List<string> { "sdw-plugin-signature-v2" };
        Add(lines, "id", manifest.Id);
        Add(lines, "name", manifest.Name);
        Add(lines, "description", manifest.Description ?? string.Empty);
        Add(lines, "version", manifest.Version);
        Add(lines, "api", manifest.ApiVersion);
        Add(lines, "entry", manifest.EntryPoint);
        foreach (var file in (manifest.Integrity?.Files ?? new Dictionary<string, string>())
                     .OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            Add(lines, "file-path", file.Key);
            Add(lines, "file-sha256", file.Value.ToLowerInvariant());
        }
        Add(lines, "data-version", manifest.DataVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add(lines, "migration-strategy", manifest.DataMigration?.Strategy ?? string.Empty);
        Add(lines, "migration-description", manifest.DataMigration?.Description ?? string.Empty);
        Add(lines, "notifications", manifest.Capabilities.Notifications ? "1" : "0");
        Add(lines, "download-control", manifest.Capabilities.DownloadControl ? "1" : "0");
        Add(lines, "storage-access", manifest.Capabilities.StorageAccess ? "1" : "0");
        Add(lines, "background-tasks", manifest.Capabilities.BackgroundTasks ? "1" : "0");
        foreach (var value in manifest.Capabilities.NetworkDomains.Order(StringComparer.OrdinalIgnoreCase))
            Add(lines, "network-domain", value.ToLowerInvariant());
        foreach (var value in manifest.Capabilities.FileRoots.Order(StringComparer.Ordinal))
            Add(lines, "file-root", value);
        foreach (var value in manifest.Platforms.Order(StringComparer.OrdinalIgnoreCase))
            Add(lines, "platform", value.ToLowerInvariant());
        foreach (var dependency in manifest.Dependencies
                     .OrderBy(value => value.Id, StringComparer.Ordinal)
                     .ThenBy(value => value.MinimumVersion, StringComparer.Ordinal))
        {
            Add(lines, "dependency-id", dependency.Id);
            Add(lines, "dependency-version", dependency.MinimumVersion);
        }
        foreach (var provider in manifest.Providers
                     .OrderBy(value => value.Kind, StringComparer.Ordinal)
                     .ThenBy(value => value.Name, StringComparer.Ordinal))
        {
            Add(lines, "provider-kind", provider.Kind);
            Add(lines, "provider-name", provider.Name);
            foreach (var handler in provider.Handlers.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                Add(lines, "handler-operation", handler.Key);
                Add(lines, "handler-name", handler.Value);
            }
        }
        return Encoding.UTF8.GetBytes(string.Join('\n', lines));
    }

    private static void Add(ICollection<string> lines, string name, string value)
        => lines.Add($"{name}:{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}");
}
