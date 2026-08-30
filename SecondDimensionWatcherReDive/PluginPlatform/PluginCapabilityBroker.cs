using System.Net;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed class PluginCapabilityBroker(
    IHttpClientFactory httpClientFactory,
    IOptions<PluginPlatformOptions> options,
    PluginSafeFileAccess fileAccess) : IPluginCapabilityBroker
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PluginPlatformOptions _options = options.Value;
    private readonly string _dataRoot = Path.Combine(Path.GetFullPath(options.Value.RootPath), "data");
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _dataGates = new(StringComparer.Ordinal);

    public Task<JsonElement> ExecuteAsync(
        PluginCatalogEntry plugin,
        string capability,
        JsonElement payload,
        CancellationToken cancellationToken)
        => capability switch
        {
            "network.request" => NetworkRequestAsync(plugin, payload, cancellationToken),
            "file.read" => ReadFileAsync(plugin, payload, cancellationToken),
            "file.list" => ListFilesAsync(plugin, payload, cancellationToken),
            "data.read" => ReadDataAsync(plugin, payload, cancellationToken),
            "data.write" => WriteDataAsync(plugin, payload, cancellationToken),
            "data.list" => ListDataAsync(plugin, payload, cancellationToken),
            "data.exists" => DataExistsAsync(plugin, payload, cancellationToken),
            "data.info" => DataInfoAsync(plugin, payload, cancellationToken),
            _ => throw new UnauthorizedAccessException($"Capability operation '{capability}' is not available.")
        };

    private async Task<JsonElement> NetworkRequestAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<NetworkCapabilityRequest>(WebJsonOptions)
                      ?? throw new InvalidDataException("Invalid network request.");
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
            throw new UnauthorizedAccessException("Only absolute HTTP(S) URLs are allowed.");
        if (!IsDomainAllowed(uri.IdnHost, plugin.ApprovedCapabilities.NetworkDomains))
            throw new UnauthorizedAccessException($"Network target '{uri.IdnHost}' was not approved.");
        if (!Enum.TryParse<HttpMethodName>(request.Method, ignoreCase: true, out var methodName))
            throw new InvalidDataException("Unsupported HTTP method.");

        using var message = new HttpRequestMessage(new HttpMethod(methodName.ToString().ToUpperInvariant()), uri);
        if (request.Body is not null)
            message.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType ?? "application/json");
        using var response = await httpClientFactory.CreateClient("PluginPlatform").SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var body = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            _options.MaximumResponseBytes, cancellationToken);
        return JsonSerializer.SerializeToElement(new
        {
            status = (int)response.StatusCode,
            contentType = response.Content.Headers.ContentType?.ToString(),
            body = Encoding.UTF8.GetString(body)
        });
    }

    private async Task<JsonElement> ReadFileAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var path = GetRequiredString(payload, "path");
        var (root, resolved) = ResolveApprovedFilePath(path, plugin.ApprovedCapabilities.FileRoots);
        var bytes = await fileAccess.ReadAsync(root, resolved, _options.MaximumResponseBytes, cancellationToken);
        return JsonSerializer.SerializeToElement(new { base64 = Convert.ToBase64String(bytes) });
    }

    private Task<JsonElement> ListFilesAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (root, path) = ResolveApprovedFilePath(GetRequiredString(payload, "path"),
            plugin.ApprovedCapabilities.FileRoots);
        var entries = fileAccess.List(root, path, 1_000)
            .Select(item => new
            {
                name = item.Name,
                isDirectory = item.IsDirectory
            })
            .ToArray();
        return Task.FromResult(JsonSerializer.SerializeToElement(entries));
    }

    private async Task<JsonElement> ReadDataAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        EnsureStorageCapability(plugin);
        var root = GetPluginDataRoot(plugin.Manifest.Id);
        var path = ResolvePluginDataPath(plugin.Manifest.Id, GetRequiredString(payload, "path"));
        var bytes = await fileAccess.ReadAsync(root, path, _options.MaximumResponseBytes, cancellationToken);
        return JsonSerializer.SerializeToElement(new { base64 = Convert.ToBase64String(bytes) });
    }

    private async Task<JsonElement> WriteDataAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        EnsureStorageCapability(plugin);
        var root = GetPluginDataRoot(plugin.Manifest.Id);
        var path = ResolvePluginDataPath(plugin.Manifest.Id, GetRequiredString(payload, "path"));
        var base64 = GetRequiredString(payload, "base64");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("Data payload must be valid base64.");
        }
        if (bytes.Length > _options.MaximumResponseBytes)
            throw new InvalidDataException("Data write exceeds the configured size limit.");
        var gate = _dataGates.GetOrAdd(plugin.Manifest.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var usage = MeasureUsage(root);
            var existing = fileAccess.Info(root, path);
            var projectedFiles = usage.Files + (existing is null ? 1 : 0);
            var projectedBytes = checked(usage.Bytes - (existing?.Length ?? 0) + bytes.Length);
            if (projectedFiles > _options.MaximumPluginDataFiles ||
                projectedBytes > _options.MaximumPluginDataBytes)
                throw new InvalidDataException("Plugin data quota would be exceeded.");
            await fileAccess.WriteAsync(root, path, bytes, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return JsonSerializer.SerializeToElement(new { written = bytes.Length });
    }

    private Task<JsonElement> ListDataAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        EnsureStorageCapability(plugin);
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetPluginDataRoot(plugin.Manifest.Id);
        var path = ResolvePluginDataPath(plugin.Manifest.Id, GetRequiredString(payload, "path", allowEmpty: true));
        if (!fileAccess.Exists(root, path))
            return Task.FromResult(JsonSerializer.SerializeToElement(Array.Empty<object>()));
        var entries = fileAccess.List(root, path, 1_000)
            .Select(item => new
            {
                name = item.Name,
                isDirectory = item.IsDirectory,
                length = item.Length,
                lastModifiedUtc = item.LastModifiedUtc
            })
            .ToArray();
        return Task.FromResult(JsonSerializer.SerializeToElement(entries));
    }

    private Task<JsonElement> DataExistsAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        EnsureStorageCapability(plugin);
        cancellationToken.ThrowIfCancellationRequested();
        var root = GetPluginDataRoot(plugin.Manifest.Id);
        var path = ResolvePluginDataPath(plugin.Manifest.Id, GetRequiredString(payload, "path", allowEmpty: true));
        var info = fileAccess.Info(root, path);
        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            exists = info is not null,
            isDirectory = info?.IsDirectory ?? false
        }));
    }

    private Task<JsonElement> DataInfoAsync(
        PluginCatalogEntry plugin,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        EnsureStorageCapability(plugin);
        cancellationToken.ThrowIfCancellationRequested();
        var relativePath = GetRequiredString(payload, "path", allowEmpty: true);
        var root = GetPluginDataRoot(plugin.Manifest.Id);
        var path = ResolvePluginDataPath(plugin.Manifest.Id, relativePath);
        var info = fileAccess.Info(root, path)
                   ?? throw new FileNotFoundException("Plugin data path does not exist.");
        return Task.FromResult(JsonSerializer.SerializeToElement(new
        {
            isDirectory = info.IsDirectory,
            path = relativePath,
            fileName = info.Name,
            length = info.Length,
            lastModifiedUtc = info.LastModifiedUtc
        }));
    }

    private (string Root, string Path) ResolveApprovedFilePath(string path, IReadOnlyList<string> approvedRoots)
    {
        if (approvedRoots.Count == 0) throw new UnauthorizedAccessException("No file roots were approved.");
        if (!Path.IsPathFullyQualified(path)) throw new UnauthorizedAccessException("File paths must be absolute.");
        var candidate = Path.GetFullPath(path);
        var root = approvedRoots.Select(Path.GetFullPath).FirstOrDefault(value => IsWithin(candidate, value));
        if (root is null) throw new UnauthorizedAccessException($"File path '{path}' is outside approved roots.");
        return (root, candidate);
    }

    private string ResolvePluginDataPath(string pluginId, string relativePath)
    {
        if (!PluginManifestValidator.IsSafeRelativePath(relativePath) && !string.IsNullOrEmpty(relativePath))
            throw new UnauthorizedAccessException("Plugin data paths must be relative and cannot contain traversal.");
        var root = GetPluginDataRoot(pluginId);
        var depth = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).Length;
        if (depth > _options.MaximumPluginDataPathDepth)
            throw new InvalidDataException("Plugin data path exceeds the configured depth limit.");
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsWithin(candidate, root)) throw new UnauthorizedAccessException("Plugin data path escapes its root.");
        return candidate;
    }

    private string GetPluginDataRoot(string pluginId) => Path.Combine(_dataRoot, pluginId);

    private static void EnsureStorageCapability(PluginCatalogEntry plugin)
    {
        if (!plugin.ApprovedCapabilities.StorageAccess)
            throw new UnauthorizedAccessException("Storage access was not approved for this plugin.");
    }

    private static string GetRequiredString(JsonElement payload, string property, bool allowEmpty = false)
    {
        if (!payload.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Capability request requires string property '{property}'.");
        var result = value.GetString() ?? string.Empty;
        if (!allowEmpty && string.IsNullOrWhiteSpace(result))
            throw new InvalidDataException($"Capability request property '{property}' cannot be empty.");
        return result;
    }

    private static bool IsDomainAllowed(string host, IEnumerable<string> domains)
        => domains.Any(pattern => pattern.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase) &&
              host.Length > pattern.Length - 1
            : host.Equals(pattern, StringComparison.OrdinalIgnoreCase));

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private (long Bytes, int Files) MeasureUsage(string root)
    {
        if (!Directory.Exists(root)) return (0, 0);
        long bytes = 0;
        var files = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in fileAccess.List(root, directory, _options.MaximumPluginDataFiles + 1))
            {
                var path = Path.Combine(directory, entry.Name);
                if (entry.IsDirectory) pending.Push(path);
                else
                {
                    files = checked(files + 1);
                    bytes = checked(bytes + (entry.Length ?? 0));
                }
            }
        }
        return (bytes, files);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException("Capability response exceeds the configured size limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.ToArray();
    }

    private sealed record NetworkCapabilityRequest(
        string Method,
        string Url,
        string? Body,
        string? ContentType);

    private enum HttpMethodName
    {
        Get,
        Post,
        Put,
        Patch,
        Delete
    }
}
