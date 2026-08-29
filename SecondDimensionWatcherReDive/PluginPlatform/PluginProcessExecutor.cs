using System.Diagnostics;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal interface IPluginProcessExecutor
{
    Task<JsonElement> InvokeAsync(
        PluginCatalogEntry plugin,
        string handler,
        JsonElement input,
        CancellationToken cancellationToken);
}

internal sealed class PluginCapacityExceededException(string message) : InvalidOperationException(message);

internal sealed class PluginProcessExecutor(
    IPluginCapabilityBroker capabilityBroker,
    IOptions<PluginPlatformOptions> options) : IPluginProcessExecutor
{
    private readonly PluginPlatformOptions _options = options.Value;
    private readonly SemaphoreSlim _globalGate = new(
        options.Value.MaximumConcurrentWorkers,
        options.Value.MaximumConcurrentWorkers);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pluginGates = new(StringComparer.Ordinal);

    public async Task<JsonElement> InvokeAsync(
        PluginCatalogEntry plugin,
        string handler,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        var pluginGate = _pluginGates.GetOrAdd(plugin.Manifest.Id, _ => new SemaphoreSlim(
            _options.MaximumConcurrentWorkersPerPlugin,
            _options.MaximumConcurrentWorkersPerPlugin));
        if (!await pluginGate.WaitAsync(0, cancellationToken))
            throw new PluginCapacityExceededException(
                $"Plugin '{plugin.Manifest.Id}' has reached its concurrent worker limit.");
        try
        {
            if (!await _globalGate.WaitAsync(0, cancellationToken))
                throw new PluginCapacityExceededException("The global plugin worker limit has been reached.");
            try
            {
                return await InvokeCoreAsync(plugin, handler, input, cancellationToken);
            }
            finally
            {
                _globalGate.Release();
            }
        }
        finally
        {
            pluginGate.Release();
        }
    }

    private async Task<JsonElement> InvokeCoreAsync(
        PluginCatalogEntry plugin,
        string handler,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        var entryBytes = await ReadAndVerifyPackageAsync(plugin, cancellationToken);
        var script = System.Text.Encoding.UTF8.GetString(entryBytes);
        using var configDocument = JsonDocument.Parse(plugin.ConfigurationJson);
        var invocation = new PluginWorkerInvocation(
            script,
            handler,
            input.Clone(),
            configDocument.RootElement.Clone(),
            Math.Clamp(_options.MaximumWorkerMemoryMegabytes / 4, 16, 64),
            _options.MaximumResponseBytes);

        using var process = new Process { StartInfo = CreateStartInfo() };
        if (!process.Start()) throw new InvalidOperationException("Could not start plugin worker process.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(_options.InvocationTimeoutMilliseconds));
        var resourceViolation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = MonitorProcessAsync(process, resourceViolation, timeout.Token);

        try
        {
            var invocationJson = JsonSerializer.Serialize(
                invocation,
                PluginWorkerJsonContext.Default.PluginWorkerInvocation);
            await process.StandardInput.WriteLineAsync(invocationJson.AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line is null)
                {
                    if (resourceViolation.Task.IsCompletedSuccessfully)
                        throw new TimeoutException(resourceViolation.Task.Result);
                    var stderr = await ReadErrorAsync(process);
                    throw new InvalidOperationException(
                        $"Plugin worker exited before returning a result (exit {process.ExitCode}): {stderr}");
                }
                if (line.Length > _options.MaximumResponseBytes * 2)
                    throw new InvalidDataException("Plugin worker protocol message is too large.");
                var message = JsonSerializer.Deserialize(
                                  line,
                                  PluginWorkerJsonContext.Default.PluginWorkerMessage)
                              ?? throw new InvalidDataException("Plugin worker sent invalid protocol data.");
                switch (message.Type)
                {
                    case "capability":
                        await HandleCapabilityAsync(process, plugin, message, timeout.Token);
                        break;
                    case "result" when message.Result is not null:
                        return message.Result.Value.Clone();
                    case "error":
                        throw new InvalidOperationException(message.Error ?? "Plugin execution failed.");
                    default:
                        throw new InvalidDataException($"Unexpected plugin worker message '{message.Type}'.");
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Plugin invocation exceeded {_options.InvocationTimeoutMilliseconds} ms or a resource limit.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            timeout.Cancel();
            try { await monitor; } catch (OperationCanceledException) { }
        }
    }

    private async Task HandleCapabilityAsync(
        Process process,
        PluginCatalogEntry plugin,
        PluginWorkerMessage message,
        CancellationToken cancellationToken)
    {
        PluginWorkerMessage response;
        try
        {
            if (message.Id is null || message.Capability is null || message.Payload is null)
                throw new InvalidDataException("Capability request is incomplete.");
            var result = await capabilityBroker.ExecuteAsync(
                plugin,
                message.Capability,
                message.Payload.Value,
                cancellationToken);
            response = new PluginWorkerMessage
            {
                Type = "capability-result",
                Id = message.Id,
                Result = result
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            response = new PluginWorkerMessage
            {
                Type = "capability-error",
                Id = message.Id,
                Error = exception.Message.Length <= 1_024 ? exception.Message : exception.Message[..1_024]
            };
        }

        var json = JsonSerializer.Serialize(response, PluginWorkerJsonContext.Default.PluginWorkerMessage);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private async Task MonitorProcessAsync(
        Process process,
        TaskCompletionSource<string> resourceViolation,
        CancellationToken cancellationToken)
    {
        var maximumWorkingSet = (long)_options.MaximumWorkerMemoryMegabytes * 1024 * 1024;
        var maximumCpu = TimeSpan.FromMilliseconds(_options.MaximumWorkerCpuMilliseconds);
        while (!process.HasExited)
        {
            cancellationToken.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.WorkingSet64 > maximumWorkingSet || process.TotalProcessorTime > maximumCpu)
            {
                resourceViolation.TrySetResult(
                    $"Plugin worker exceeded its CPU or {_options.MaximumWorkerMemoryMegabytes} MiB memory budget.");
                process.Kill(entireProcessTree: true);
                return;
            }
            await Task.Delay(25, cancellationToken);
        }
    }

    private static ProcessStartInfo CreateStartInfo()
    {
        var hostAssembly = typeof(PluginWorkerHost).Assembly;
        var entryAssembly = Assembly.GetEntryAssembly();
        var processPath = entryAssembly == hostAssembly
            ? Environment.ProcessPath
            : null;
        processPath ??= "dotnet";
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (entryAssembly != hostAssembly ||
            string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(hostAssembly.Location);
        startInfo.ArgumentList.Add(PluginWorkerHost.WorkerArgument);
        return startInfo;
    }

    private static async Task<string> ReadErrorAsync(Process process)
    {
        var value = await process.StandardError.ReadToEndAsync();
        return value.Length <= 1_024 ? value : value[..1_024];
    }

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private static async Task<byte[]> ReadAndVerifyPackageAsync(
        PluginCatalogEntry plugin,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(plugin.PackageDirectory);
        if (!Directory.Exists(root) || new DirectoryInfo(root).LinkTarget is not null)
            throw new InvalidDataException("Installed plugin package directory is missing or unsafe.");
        var expected = plugin.Manifest.Integrity?.Files
                       ?? throw new InvalidDataException("Installed plugin integrity metadata is missing.");
        var actualPaths = new HashSet<string>(StringComparer.Ordinal);
        byte[]? entryBytes = null;
        var entryPath = plugin.Manifest.EntryPoint.Replace('\\', '/');

        foreach (var path in EnumerateRegularFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Equals("manifest.json", StringComparison.Ordinal)) continue;
            actualPaths.Add(relative);
            if (!expected.TryGetValue(relative, out var expectedDigest))
                throw new InvalidDataException($"Installed plugin contains unlisted file '{relative}'.");
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var actualDigest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actualDigest.Equals(expectedDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Installed plugin file '{relative}' failed its integrity check.");
            if (relative.Equals(entryPath, StringComparison.Ordinal)) entryBytes = bytes;
        }

        var missing = expected.Keys.Except(actualPaths, StringComparer.Ordinal).Order().ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Installed plugin files are missing: {string.Join(", ", missing)}.");
        return entryBytes ?? throw new InvalidDataException("Installed plugin entry point is missing or unsafe.");
    }

    private static IEnumerable<string> EnumerateRegularFiles(string root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.LinkTarget is not null)
                    throw new InvalidDataException("Symbolic links are not allowed in installed plugin packages.");
                if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                }
                else if (entry is FileInfo)
                {
                    yield return entry.FullName;
                }
            }
        }
    }
}
