using System.Runtime.CompilerServices;
using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal static class PluginProviderIdentity
{
    public static string Create(string pluginId, string providerName)
        => $"plugin:{pluginId}:{providerName}";
}

public interface IPluginProviderRegistry
{
    IReadOnlyList<IFileStore> GetFileStores();
    IReadOnlyList<INotificationProvider> GetNotificationProviders();
}

internal sealed class PluginProviderRegistry(IPluginManager manager) : IPluginProviderRegistry
{
    public IReadOnlyList<IFileStore> GetFileStores()
        => manager.GetSnapshot()
            .Where(IsAvailable)
            .SelectMany(plugin => plugin.Manifest.Providers
                .Where(provider => provider.Kind == "storage")
                .Select(provider => (IFileStore)new JavaScriptFileStore(
                    plugin.Manifest.Id,
                    provider,
                    manager)))
            .ToArray();

    public IReadOnlyList<INotificationProvider> GetNotificationProviders()
        => manager.GetSnapshot()
            .Where(IsAvailable)
            .SelectMany(plugin => plugin.Manifest.Providers
                .Where(provider => provider.Kind == "notification")
                .Select(provider => (INotificationProvider)new JavaScriptNotificationProvider(
                    plugin.Manifest.Id,
                    provider,
                    manager)))
            .ToArray();

    private static bool IsAvailable(InstalledPlugin plugin)
        => plugin.IsEnabled && plugin.CompatibilityErrors.Count == 0 &&
           (plugin.Health.CircuitOpenUntil is null || plugin.Health.CircuitOpenUntil <= DateTimeOffset.UtcNow);
}

internal sealed class JavaScriptNotificationProvider(
    string pluginId,
    PluginProviderDeclaration declaration,
    IPluginManager manager) : INotificationProvider
{
    public string Name => PluginProviderIdentity.Create(pluginId, declaration.Name);

    public async Task SendAsync(PluginNotification notification, CancellationToken cancellationToken)
    {
        if (!declaration.Handlers.TryGetValue("send", out var handler))
            throw new InvalidOperationException($"Notification provider '{Name}' has no send handler.");
        var input = JsonSerializer.SerializeToElement(notification);
        var result = await manager.InvokeAsync(pluginId, handler, input, cancellationToken);
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException($"Notification provider '{Name}' rejected the notification.");
    }
}

internal sealed class JavaScriptFileStore(
    string pluginId,
    PluginProviderDeclaration declaration,
    IPluginManager manager) : IFileStore
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    public string Name => PluginProviderIdentity.Create(pluginId, declaration.Name);

    public async Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        var result = await InvokeAsync("read", path, cancellationToken);
        if (!result.TryGetProperty("base64", out var base64) || base64.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Storage provider read result must contain base64 data.");
        return new MemoryStream(Convert.FromBase64String(base64.GetString()!), writable: false);
    }

    public async Task<FileStoreInfo> FileInfoAsync(string path, CancellationToken cancellationToken)
    {
        var result = await InvokeAsync("info", path, cancellationToken);
        return result.Deserialize<FileStoreInfo>(WebJsonOptions)
               ?? throw new InvalidDataException("Storage provider returned invalid file information.");
    }

    public async Task<bool> ExistAsync(string path, CancellationToken cancellationToken)
    {
        var result = await InvokeAsync("exists", path, cancellationToken);
        return result.TryGetProperty("exists", out var exists) && exists.GetBoolean();
    }

    public IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path)
        => EnumerateDirectoryCore(path, CancellationToken.None);

    private async IAsyncEnumerable<FileStoreInfo> EnumerateDirectoryCore(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var result = await InvokeAsync("list", path, cancellationToken);
        var entries = result.Deserialize<FileStoreInfo[]>(WebJsonOptions)
                      ?? throw new InvalidDataException("Storage provider returned an invalid directory listing.");
        foreach (var entry in entries) yield return entry;
    }

    private Task<JsonElement> InvokeAsync(string operation, string path, CancellationToken cancellationToken)
    {
        if (!declaration.Handlers.TryGetValue(operation, out var handler))
            throw new InvalidOperationException($"Storage provider '{Name}' has no {operation} handler.");
        return manager.InvokeAsync(pluginId, handler, JsonSerializer.SerializeToElement(new { path }), cancellationToken);
    }
}
