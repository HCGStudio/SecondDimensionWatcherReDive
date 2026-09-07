using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.PluginPlatform;

namespace SecondDimensionWatcherReDive.Repositories;

internal sealed class PluginCatalogRepository : IPluginCatalogRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _catalogPath;
    private readonly string _retainedPath;

    public PluginCatalogRepository(IOptions<PluginPlatformOptions> options)
    {
        var root = Path.GetFullPath(options.Value.RootPath);
        Directory.CreateDirectory(root);
        RestrictDirectory(root);
        _catalogPath = Path.Combine(root, "catalog");
        _retainedPath = Path.Combine(root, "retained");
        Directory.CreateDirectory(_catalogPath);
        Directory.CreateDirectory(_retainedPath);
        RestrictDirectory(_catalogPath);
        RestrictDirectory(_retainedPath);
    }

    public async Task<IReadOnlyList<PluginCatalogEntry>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var result = new List<PluginCatalogEntry>();
            foreach (var path in Directory.EnumerateFiles(_catalogPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                await using var stream = File.OpenRead(path);
                var entry = await JsonSerializer.DeserializeAsync<PluginCatalogEntry>(stream, JsonOptions,
                    cancellationToken);
                if (entry is not null) result.Add(entry);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginCatalogEntry?> FindAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<PluginCatalogEntry>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PluginCatalogEntry entry, CancellationToken cancellationToken)
    {
        var path = GetPath(entry.Manifest.Id);
        var temporaryPath = Path.Combine(_catalogPath, $".{entry.Manifest.Id}.{Guid.NewGuid():N}.tmp");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            RestrictFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetPath(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RetainedPluginData?> FindRetainedAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetRetainedPath(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<RetainedPluginData>(stream, JsonOptions, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveRetainedAsync(RetainedPluginData retained, CancellationToken cancellationToken)
    {
        var path = GetRetainedPath(retained.Id);
        var temporaryPath = Path.Combine(_retainedPath, $".{retained.Id}.{Guid.NewGuid():N}.tmp");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, retained, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            RestrictFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _gate.Release();
        }
    }

    public async Task RemoveRetainedAsync(string id, CancellationToken cancellationToken)
    {
        var path = GetRetainedPath(id);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(string id)
    {
        if (!PluginManifestValidator.IsValidId(id)) throw new ArgumentException("Invalid plugin id.", nameof(id));
        return Path.Combine(_catalogPath, $"{id}.json");
    }

    private string GetRetainedPath(string id)
    {
        if (!PluginManifestValidator.IsValidId(id)) throw new ArgumentException("Invalid plugin id.", nameof(id));
        return Path.Combine(_retainedPath, $"{id}.json");
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
