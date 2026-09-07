using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed class PluginManager(
    IPluginCatalogRepository repository,
    PluginPackageInspector packageInspector,
    IPluginProcessExecutor processExecutor,
    IOptions<PluginPlatformOptions> options,
    TimeProvider timeProvider) : IPluginManager, IJavaScriptPluginLoader
{
    private const string LifecycleJournalSuffix = ".journal.json";

    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly PluginLifecycleCoordinator _lifecycle = new();
    private readonly Dictionary<string, PluginCatalogEntry> _entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingLifecyclePluginIds = new(StringComparer.Ordinal);
    private InstalledPlugin[] _snapshot = [];
    private readonly PluginPlatformOptions _options = options.Value;
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);
    private bool _initialized;

    internal Action? BeforeInvocationLeaseForTesting { get; set; }
    internal Action<PluginLifecycleCheckpoint>? LifecycleCheckpointForTesting { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(_rootPath);
            RestrictDirectory(_rootPath);
            await RecoverLifecycleTransactionsAsync(cancellationToken);
            foreach (var entry in await repository.GetAllAsync(cancellationToken))
                _entries[entry.Manifest.Id] = entry;
            await DisableMissingPackagePluginsAsync(cancellationToken);
            await DisableIncompatiblePluginsAsync(cancellationToken);
            CleanupUnreferencedPackages();
            UpdateSnapshot();
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<InstalledPlugin>> GetAllAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            UpdateSnapshot();
            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<InstalledPlugin> GetSnapshot() => Volatile.Read(ref _snapshot);

    public async Task<PluginPackagePreview> PreviewPackageAsync(
        Stream package,
        string fileName,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var inspected = await packageInspector.StageAndInspectAsync(package, fileName, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return new PluginPackagePreview(
                inspected.Token,
                inspected.PackageSha256,
                inspected.Manifest,
                GetCompatibilityErrors(inspected.Manifest),
                inspected.IsSignatureTrusted,
                inspected.SignatureStatus,
                inspected.ExpiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PluginInstallResult> InstallPackageAsync(
        string previewToken,
        string expectedSha256,
        PluginCapabilities approvedCapabilities,
        CancellationToken cancellationToken)
        => InstallOrUpgradeAsync(null, previewToken, expectedSha256, approvedCapabilities, cancellationToken);

    public Task<PluginInstallResult> UpgradeAsync(
        string id,
        string previewToken,
        string expectedSha256,
        PluginCapabilities approvedCapabilities,
        CancellationToken cancellationToken)
        => InstallOrUpgradeAsync(id, previewToken, expectedSha256, approvedCapabilities, cancellationToken);

    public async Task EnableAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = GetRequiredEntry(id);
            EnsureNoPendingLifecycleManagement();
            var errors = GetCompatibilityErrors(entry.Manifest);
            if (errors.Count > 0)
                throw new InvalidOperationException($"Plugin cannot be enabled: {string.Join(" ", errors)}");
            if (entry.IsEnabled) return;
            entry = entry with
            {
                IsEnabled = true,
                Health = entry.Health with
                {
                    Status = "healthy",
                    ConsecutiveFailures = 0,
                    CircuitOpenUntil = null,
                    LastError = null
                }
            };
            await repository.SaveAsync(entry, cancellationToken);
            _entries[id] = entry;
            UpdateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisableAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = GetRequiredEntry(id);
            EnsureNoPendingLifecycleManagement();
            using var lifecycle = await _lifecycle.BeginLifecycleAsync(
                id, LifecycleWaitTimeout, cancellationToken);
            if (entry.IsEnabled)
            {
                entry = entry with { IsEnabled = false };
                await repository.SaveAsync(entry, cancellationToken);
                _entries[id] = entry;
            }
            await DisableIncompatiblePluginsAsync(cancellationToken);
            UpdateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UninstallAsync(string id, bool deleteData, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        string? transactionPath = null;
        string? packageBackupPath = null;
        string? dataBackupPath = null;
        RetainedPluginData? originalRetained = null;
        PluginLifecycleJournal? journal = null;
        var journalCommitted = false;
        var originalEntries = _entries.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        try
        {
            var entry = GetRequiredEntry(id);
            EnsureNoPendingLifecycleManagement();
            using var lifecycle = await _lifecycle.BeginLifecycleAsync(
                id, LifecycleWaitTimeout, cancellationToken);
            originalRetained = await repository.FindRetainedAsync(id, cancellationToken);
            var intendedRetained = deleteData
                ? null
                : new RetainedPluginData(
                    id,
                    entry.ConfigurationJson,
                    entry.DataVersion,
                    timeProvider.GetUtcNow(),
                    entry.PublisherFingerprint);
            transactionPath = CreateTransactionPath(PluginLifecycleJournalValues.Uninstall, id);
            journal = new PluginLifecycleJournal(
                PluginLifecycleJournalValues.Uninstall,
                id,
                PluginLifecycleJournalValues.Prepared,
                originalEntries.Values.ToArray(),
                originalRetained,
                intendedRetained,
                deleteData);
            WriteLifecycleJournal(transactionPath, journal);
            packageBackupPath = MoveToTransaction(entry.PackageDirectory,
                Path.Combine(transactionPath, "package"));
            if (deleteData)
                dataBackupPath = MoveToTransaction(GetDataPath(id), Path.Combine(transactionPath, "data"));
            LifecycleCheckpointForTesting?.Invoke(PluginLifecycleCheckpoint.AfterMove);

            if (!deleteData)
            {
                await repository.SaveRetainedAsync(intendedRetained!, cancellationToken);
            }
            else
            {
                await repository.RemoveRetainedAsync(id, cancellationToken);
            }

            await repository.RemoveAsync(id, cancellationToken);
            _entries.Remove(id);
            await DisableIncompatiblePluginsAsync(cancellationToken);
            UpdateSnapshot();
            journal = journal with { Phase = PluginLifecycleJournalValues.Committed };
            WriteLifecycleJournal(transactionPath, journal);
            journalCommitted = true;
            LifecycleCheckpointForTesting?.Invoke(PluginLifecycleCheckpoint.AfterCommit);
            DeleteLifecycleTransaction(transactionPath, id);
        }
        catch (PluginProcessCrashSimulationException)
        {
            throw;
        }
        catch (Exception) when (journalCommitted)
        {
            // The catalog change is durably committed. Keep the journal so startup can
            // retry idempotent cleanup rather than attempting an unsafe partial rollback.
            throw;
        }
        catch (Exception failure)
        {
            try
            {
                foreach (var originalEntry in originalEntries.Values)
                    await repository.SaveAsync(originalEntry, CancellationToken.None);
                if (originalRetained is null)
                    await repository.RemoveRetainedAsync(id, CancellationToken.None);
                else
                    await repository.SaveRetainedAsync(originalRetained, CancellationToken.None);
                RestoreTransactionDirectory(packageBackupPath,
                    originalEntries.TryGetValue(id, out var original) ? original.PackageDirectory : null);
                RestoreTransactionDirectory(dataBackupPath, GetDataPath(id));
                _entries.Clear();
                foreach (var originalEntry in originalEntries)
                    _entries[originalEntry.Key] = originalEntry.Value;
                UpdateSnapshot();
                DeleteLifecycleTransactionBestEffort(transactionPath, id);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Plugin uninstall failed and rollback was incomplete.",
                    failure, rollbackFailure);
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateConfigurationAsync(
        string id,
        JsonElement configuration,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (configuration.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Plugin configuration must be a JSON object.");
        var json = configuration.GetRawText();
        if (json.Length > 64 * 1024) throw new InvalidDataException("Plugin configuration is too large.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = GetRequiredEntry(id) with { ConfigurationJson = json };
            EnsureNoPendingLifecycleManagement();
            await repository.SaveAsync(entry, cancellationToken);
            _entries[id] = entry;
            UpdateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JsonElement> InvokeAsync(
        string id,
        string handler,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!PluginManifestValidator.IsValidHandlerName(handler))
            throw new InvalidDataException("Invalid plugin handler name.");
        PluginCatalogEntry entry;
        PluginLifecycleCoordinator.InvocationLease? invocation = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            entry = GetRequiredEntry(id);
            EnsureNoPendingLifecycleManagement();
            if (!entry.IsEnabled) throw new InvalidOperationException($"Plugin '{id}' is disabled.");
            var errors = GetCompatibilityErrors(entry.Manifest);
            if (errors.Count > 0)
                throw new InvalidOperationException($"Plugin '{id}' is incompatible: {string.Join(" ", errors)}");
            if (entry.Health.CircuitOpenUntil is { } openUntil && openUntil > timeProvider.GetUtcNow())
                throw new InvalidOperationException($"Plugin '{id}' circuit is open until {openUntil:O}.");
            BeforeInvocationLeaseForTesting?.Invoke();
            invocation = _lifecycle.EnterInvocation(id, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        JsonElement result = default;
        Exception? failure = null;
        var interruptedByLifecycle = false;
        using (var activeInvocation = invocation ?? throw new UnreachableException())
        {
            try
            {
                result = await processExecutor.InvokeAsync(entry, handler, input, activeInvocation.Token);
                interruptedByLifecycle = activeInvocation.LifecycleCancellationToken.IsCancellationRequested &&
                                         !cancellationToken.IsCancellationRequested;
                if (interruptedByLifecycle)
                    failure = new OperationCanceledException("Invocation completed during lifecycle cancellation.");
            }
            catch (Exception exception)
            {
                failure = exception;
                interruptedByLifecycle = activeInvocation.LifecycleCancellationToken.IsCancellationRequested &&
                                         !cancellationToken.IsCancellationRequested;
            }
        }

        if (failure is null)
        {
            await RecordSuccessAsync(id, cancellationToken);
            return result;
        }

        if (interruptedByLifecycle)
            throw new PluginInvocationInterruptedException(
                $"Plugin '{id}' invocation was cancelled by a lifecycle operation.");
        if (failure is not PluginCapacityExceededException &&
            (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            await RecordFailureAsync(id, failure, CancellationToken.None);
        ExceptionDispatchInfo.Capture(failure).Throw();
        throw new UnreachableException();
    }

    private async Task<PluginInstallResult> InstallOrUpgradeAsync(
        string? upgradeId,
        string previewToken,
        string expectedSha256,
        PluginCapabilities approvedCapabilities,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var inspected = await packageInspector.InspectStagedAsync(previewToken, expectedSha256, cancellationToken);
        if (!PluginManifestValidator.CapabilitiesEqual(inspected.Manifest.Capabilities, approvedCapabilities))
            throw new UnauthorizedAccessException("Approved capabilities do not exactly match the reviewed manifest.");
        if (!inspected.IsSignatureTrusted &&
            !(_options.AllowUnsignedLocalPackages && inspected.Manifest.Signature is null))
            throw new UnauthorizedAccessException(
                $"Package is not signed by a trusted publisher. {inspected.SignatureStatus}");

        await _gate.WaitAsync(cancellationToken);
        string? extractedPath = null;
        string? transactionPath = null;
        string? dataBackupPath = null;
        PluginCatalogEntry? existing = null;
        RetainedPluginData? retained = null;
        PluginLifecycleJournal? journal = null;
        IDisposable? lifecycle = null;
        var catalogWritten = false;
        var journalCommitted = false;
        var originalEntries = _entries.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        try
        {
            _entries.TryGetValue(inspected.Manifest.Id, out existing);
            EnsureNoPendingLifecycleManagement();
            if (upgradeId is null && existing is not null)
                throw new InvalidOperationException("Plugin is already installed; use the upgrade operation.");
            if (upgradeId is not null)
            {
                if (existing is null || !string.Equals(upgradeId, inspected.Manifest.Id, StringComparison.Ordinal))
                    throw new InvalidOperationException("Upgrade package id does not match the installed plugin.");
                if (!PluginManifestValidator.TryParseVersion(existing.Manifest.Version, out var oldVersion) ||
                    !PluginManifestValidator.TryParseVersion(inspected.Manifest.Version, out var newVersion) ||
                    newVersion <= oldVersion)
                    throw new InvalidOperationException("Upgrade version must be newer than the installed version.");
                EnsurePublisherContinuity(existing.PublisherFingerprint, inspected.PublisherFingerprint);
                lifecycle = await _lifecycle.BeginLifecycleAsync(
                    inspected.Manifest.Id, LifecycleWaitTimeout, cancellationToken);
            }

            retained = existing is null
                ? await repository.FindRetainedAsync(inspected.Manifest.Id, cancellationToken)
                : null;
            if (retained is not null)
                EnsurePublisherContinuity(retained.PublisherFingerprint, inspected.PublisherFingerprint);
            var previousDataVersion = existing?.DataVersion ?? retained?.DataVersion;
            if (previousDataVersion is not null && previousDataVersion != inspected.Manifest.DataVersion)
            {
                if (!string.Equals(inspected.Manifest.DataMigration?.Strategy, "reset", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Data version changes from {previousDataVersion} to {inspected.Manifest.DataVersion}; manifest must explicitly declare dataMigration.strategy = 'reset'.");
                transactionPath = CreateTransactionPath(
                    PluginLifecycleJournalValues.Upgrade, inspected.Manifest.Id);
                journal = new PluginLifecycleJournal(
                    PluginLifecycleJournalValues.Upgrade,
                    inspected.Manifest.Id,
                    PluginLifecycleJournalValues.Prepared,
                    originalEntries.Values.ToArray(),
                    retained,
                    null,
                    DeleteData: false);
                WriteLifecycleJournal(transactionPath, journal);
                dataBackupPath = MoveToTransaction(
                    GetDataPath(inspected.Manifest.Id), Path.Combine(transactionPath, "data"));
                LifecycleCheckpointForTesting?.Invoke(PluginLifecycleCheckpoint.AfterMove);
            }

            extractedPath = await packageInspector.ExtractAsync(inspected, cancellationToken);
            var configuration = existing?.ConfigurationJson ?? retained?.ConfigurationJson ?? "{}";
            var entry = new PluginCatalogEntry(
                inspected.Manifest,
                false,
                approvedCapabilities,
                new PluginHealth("healthy", 0, null, null, null, null),
                extractedPath,
                configuration,
                inspected.Manifest.DataVersion,
                inspected.PublisherFingerprint);
            await repository.SaveAsync(entry, cancellationToken);
            _entries[inspected.Manifest.Id] = entry;
            catalogWritten = true;
            await DisableIncompatiblePluginsAsync(cancellationToken);
            await repository.RemoveRetainedAsync(inspected.Manifest.Id, cancellationToken);
            UpdateSnapshot();

            if (journal is not null)
            {
                journal = journal with { Phase = PluginLifecycleJournalValues.Committed };
                WriteLifecycleJournal(transactionPath!, journal);
                journalCommitted = true;
                LifecycleCheckpointForTesting?.Invoke(PluginLifecycleCheckpoint.AfterCommit);
                DeleteLifecycleTransaction(transactionPath, inspected.Manifest.Id);
            }

            if (existing is not null && !PathsEqual(existing.PackageDirectory, extractedPath) &&
                Directory.Exists(existing.PackageDirectory))
            {
                try { Directory.Delete(existing.PackageDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try { packageInspector.Consume(inspected); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return new PluginInstallResult(
                inspected.Manifest.Id,
                inspected.Manifest.Version,
                existing is not null,
                GetCompatibilityErrors(inspected.Manifest));
        }
        catch (PluginProcessCrashSimulationException)
        {
            throw;
        }
        catch (Exception) when (journalCommitted)
        {
            // The new catalog is committed; startup will finish journal cleanup.
            throw;
        }
        catch (Exception failure)
        {
            try
            {
                if (catalogWritten)
                {
                    if (existing is null)
                        await repository.RemoveAsync(inspected.Manifest.Id, CancellationToken.None);
                    foreach (var original in originalEntries.Values)
                        await repository.SaveAsync(original, CancellationToken.None);
                    if (retained is not null)
                        await repository.SaveRetainedAsync(retained, CancellationToken.None);
                }
                _entries.Clear();
                foreach (var original in originalEntries) _entries[original.Key] = original.Value;
                UpdateSnapshot();
                if (extractedPath is not null && Directory.Exists(extractedPath))
                    Directory.Delete(extractedPath, recursive: true);
                RestoreTransactionDirectory(dataBackupPath, GetDataPath(inspected.Manifest.Id));
                DeleteLifecycleTransactionBestEffort(transactionPath, inspected.Manifest.Id);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Plugin installation failed and rollback was incomplete.",
                    failure, rollbackFailure);
            }
            throw;
        }
        finally
        {
            lifecycle?.Dispose();
            _gate.Release();
        }
    }

    private async Task RecordSuccessAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pendingLifecyclePluginIds.Count > 0) return;
            if (!_entries.TryGetValue(id, out var entry)) return;
            entry = entry with
            {
                Health = entry.Health with
                {
                    Status = "healthy",
                    ConsecutiveFailures = 0,
                    LastSuccessAt = timeProvider.GetUtcNow(),
                    LastError = null,
                    CircuitOpenUntil = null
                }
            };
            await repository.SaveAsync(entry, cancellationToken);
            _entries[id] = entry;
            UpdateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RecordFailureAsync(string id, Exception exception, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pendingLifecyclePluginIds.Count > 0) return;
            if (!_entries.TryGetValue(id, out var entry)) return;
            var failures = checked(entry.Health.ConsecutiveFailures + 1);
            DateTimeOffset? openUntil = failures >= _options.CircuitBreakerFailures
                ? timeProvider.GetUtcNow().AddSeconds(_options.CircuitBreakerSeconds)
                : null;
            entry = entry with
            {
                Health = entry.Health with
                {
                    Status = openUntil is null ? "degraded" : "circuit-open",
                    ConsecutiveFailures = failures,
                    LastFailureAt = timeProvider.GetUtcNow(),
                    LastError = Truncate(exception.Message, 1_024),
                    CircuitOpenUntil = openUntil
                }
            };
            await repository.SaveAsync(entry, cancellationToken);
            _entries[id] = entry;
            UpdateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DisableIncompatiblePluginsAsync(CancellationToken cancellationToken)
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var pair in _entries.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray())
            {
                if (!pair.Value.IsEnabled || GetCompatibilityErrors(pair.Value.Manifest).Count == 0) continue;
                using var lifecycle = await _lifecycle.BeginLifecycleAsync(
                    pair.Key, LifecycleWaitTimeout, cancellationToken);
                var disabled = pair.Value with { IsEnabled = false };
                await repository.SaveAsync(disabled, cancellationToken);
                _entries[pair.Key] = disabled;
                changed = true;
            }
        } while (changed);
    }

    private IReadOnlyList<string> GetCompatibilityErrors(PluginManifest manifest)
    {
        var installed = _entries.ToDictionary(
            pair => pair.Key,
            pair => new PluginCatalogEntryView(pair.Value.Manifest.Version, pair.Value.IsEnabled),
            StringComparer.Ordinal);
        return PluginManifestValidator.GetCompatibilityErrors(manifest, installed);
    }

    private IReadOnlyList<InstalledPlugin> CreateSnapshot()
        => _entries.Values
            .OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new InstalledPlugin(
                entry.Manifest,
                entry.IsEnabled,
                entry.ApprovedCapabilities,
                GetCompatibilityErrors(entry.Manifest),
                entry.Health,
                ParseConfiguration(entry.ConfigurationJson)))
            .ToArray();

    private void UpdateSnapshot() => Volatile.Write(ref _snapshot, CreateSnapshot().ToArray());

    private PluginCatalogEntry GetRequiredEntry(string id)
    {
        if (!PluginManifestValidator.IsValidId(id)) throw new ArgumentException("Invalid plugin id.", nameof(id));
        return _entries.TryGetValue(id, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Plugin '{id}' is not installed.");
    }

    private void EnsureNoPendingLifecycleManagement()
    {
        if (_pendingLifecyclePluginIds.Count > 0)
            throw new InvalidOperationException(
                $"A pending lifecycle recovery exists for '{string.Join("', '", _pendingLifecyclePluginIds.Order())}'. " +
                "Restart the service to finalize it before another plugin operation.");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized) await InitializeAsync(cancellationToken);
    }

    private string CreateTransactionPath(string operation, string id)
    {
        if (!PluginManifestValidator.IsValidId(id)) throw new ArgumentException("Invalid plugin id.", nameof(id));
        var transactionRoot = Path.Combine(_rootPath, "transactions");
        Directory.CreateDirectory(transactionRoot);
        RestrictDirectory(transactionRoot);
        var transactionPath = Path.Combine(transactionRoot, $"{operation}-{id}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(transactionPath);
        RestrictDirectory(transactionPath);
        return transactionPath;
    }

    private void WriteLifecycleJournal(string transactionPath, PluginLifecycleJournal journal)
    {
        var transactionRoot = Path.GetFullPath(Path.Combine(_rootPath, "transactions"));
        var fullTransactionPath = Path.GetFullPath(transactionPath);
        if (!IsStrictlyWithin(fullTransactionPath, transactionRoot))
            throw new UnauthorizedAccessException("Plugin lifecycle transaction is outside the transaction root.");
        var journalPath = GetLifecycleJournalPath(fullTransactionPath);
        var temporaryPath = $"{journalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, journal, JournalJsonOptions);
                stream.Flush(flushToDisk: true);
            }
            RestrictFile(temporaryPath);
            File.Move(temporaryPath, journalPath, overwrite: true);
            _pendingLifecyclePluginIds.Add(journal.PluginId);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async Task RecoverLifecycleTransactionsAsync(CancellationToken cancellationToken)
    {
        var transactionRoot = Path.Combine(_rootPath, "transactions");
        if (!Directory.Exists(transactionRoot)) return;
        RestrictDirectory(transactionRoot);
        var pending = new List<(string TransactionPath, PluginLifecycleJournal Journal)>();
        foreach (var journalPath in Directory.EnumerateFiles(
                     transactionRoot, $"*{LifecycleJournalSuffix}", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionPath = journalPath[..^LifecycleJournalSuffix.Length];
            if (!IsStrictlyWithin(Path.GetFullPath(transactionPath), Path.GetFullPath(transactionRoot)))
                throw new InvalidDataException("Plugin lifecycle transaction path is invalid.");

            PluginLifecycleJournal? journal;
            await using (var stream = File.OpenRead(journalPath))
                journal = await JsonSerializer.DeserializeAsync<PluginLifecycleJournal>(
                    stream, JournalJsonOptions, cancellationToken);
            ValidateLifecycleJournal(journal, transactionPath);
            pending.Add((transactionPath, journal!));
        }

        if (pending.GroupBy(item => item.Journal.PluginId, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
            throw new InvalidDataException("Multiple pending lifecycle journals exist for the same plugin.");
        foreach (var item in pending) _pendingLifecyclePluginIds.Add(item.Journal.PluginId);

        foreach (var item in pending)
        {
            var current = await repository.FindAsync(item.Journal.PluginId, cancellationToken);
            var catalogCommitted = CatalogReflectsCommittedJournal(item.Journal, current);
            if (item.Journal.Phase == PluginLifecycleJournalValues.Committed && catalogCommitted)
                await FinalizeCommittedJournalAsync(
                    item.Journal, item.TransactionPath, cancellationToken);
            else
                await RollbackPreparedJournalAsync(
                    item.Journal, item.TransactionPath, cancellationToken);
        }

        foreach (var temporaryJournal in Directory.EnumerateFiles(
                     transactionRoot, $"*{LifecycleJournalSuffix}.*.tmp", SearchOption.TopDirectoryOnly))
            File.Delete(temporaryJournal);
        foreach (var transactionPath in Directory.EnumerateDirectories(transactionRoot).ToArray())
        {
            if (File.Exists(GetLifecycleJournalPath(transactionPath))) continue;
            if (Directory.Exists(Path.Combine(transactionPath, "package")) ||
                Directory.Exists(Path.Combine(transactionPath, "data")))
                throw new InvalidDataException(
                    $"Plugin lifecycle transaction '{Path.GetFileName(transactionPath)}' has payload but no recovery journal.");
            DeleteTransaction(transactionPath);
        }
    }

    private void ValidateLifecycleJournal(PluginLifecycleJournal? journal, string transactionPath)
    {
        if (journal is null ||
            journal.Operation is not (PluginLifecycleJournalValues.Upgrade or PluginLifecycleJournalValues.Uninstall) ||
            journal.Phase is not (PluginLifecycleJournalValues.Prepared or PluginLifecycleJournalValues.Committed) ||
            !PluginManifestValidator.IsValidId(journal.PluginId) ||
            journal.OriginalEntries.GroupBy(entry => entry.Manifest.Id, StringComparer.Ordinal)
                .Any(group => group.Count() > 1))
            throw new InvalidDataException(
                $"Plugin lifecycle transaction '{Path.GetFileName(transactionPath)}' has an invalid journal.");

        var packagesRoot = Path.GetFullPath(Path.Combine(_rootPath, "packages"));
        foreach (var entry in journal.OriginalEntries)
        {
            if (!PluginManifestValidator.IsValidId(entry.Manifest.Id) ||
                !IsStrictlyWithin(Path.GetFullPath(entry.PackageDirectory), packagesRoot))
                throw new InvalidDataException("Plugin lifecycle journal contains an invalid catalog path.");
        }

        var original = journal.OriginalEntries.SingleOrDefault(entry => entry.Manifest.Id == journal.PluginId);
        if (journal.Operation == PluginLifecycleJournalValues.Uninstall && original is null)
            throw new InvalidDataException("Uninstall recovery journal is missing the original plugin catalog entry.");
        if (journal.OriginalRetained is { } retained && retained.Id != journal.PluginId ||
            journal.IntendedRetained is { } intended && intended.Id != journal.PluginId)
            throw new InvalidDataException("Plugin lifecycle journal contains retained data for another plugin.");
    }

    private static bool CatalogReflectsCommittedJournal(
        PluginLifecycleJournal journal,
        PluginCatalogEntry? current)
    {
        if (journal.Operation == PluginLifecycleJournalValues.Uninstall) return current is null;
        if (current is null) return false;
        var original = journal.OriginalEntries.SingleOrDefault(entry => entry.Manifest.Id == journal.PluginId);
        return original is null ||
               !string.Equals(current.Manifest.Version, original.Manifest.Version, StringComparison.Ordinal) ||
               !PathsEqual(current.PackageDirectory, original.PackageDirectory);
    }

    private async Task RollbackPreparedJournalAsync(
        PluginLifecycleJournal journal,
        string transactionPath,
        CancellationToken cancellationToken)
    {
        var original = journal.OriginalEntries.SingleOrDefault(entry => entry.Manifest.Id == journal.PluginId);
        if (journal.Operation == PluginLifecycleJournalValues.Uninstall)
            RestoreTransactionDirectory(
                Path.Combine(transactionPath, "package"), original!.PackageDirectory);
        RestoreTransactionDirectory(
            Path.Combine(transactionPath, "data"), GetDataPath(journal.PluginId));

        if (journal.OriginalRetained is null)
            await repository.RemoveRetainedAsync(journal.PluginId, cancellationToken);
        else
            await repository.SaveRetainedAsync(journal.OriginalRetained, cancellationToken);

        if (original is null)
            await repository.RemoveAsync(journal.PluginId, cancellationToken);
        foreach (var entry in journal.OriginalEntries)
            await repository.SaveAsync(entry, cancellationToken);
        DeleteLifecycleTransaction(transactionPath, journal.PluginId);
    }

    private async Task FinalizeCommittedJournalAsync(
        PluginLifecycleJournal journal,
        string transactionPath,
        CancellationToken cancellationToken)
    {
        if (journal.Operation == PluginLifecycleJournalValues.Uninstall)
        {
            await repository.RemoveAsync(journal.PluginId, cancellationToken);
            if (journal.DeleteData)
            {
                await repository.RemoveRetainedAsync(journal.PluginId, cancellationToken);
                DeleteDirectoryWithinRoot(GetDataPath(journal.PluginId));
            }
            else if (journal.IntendedRetained is not null)
            {
                await repository.SaveRetainedAsync(journal.IntendedRetained, cancellationToken);
            }
        }
        else
        {
            await repository.RemoveRetainedAsync(journal.PluginId, cancellationToken);
        }

        DeleteLifecycleTransaction(transactionPath, journal.PluginId);
    }

    private async Task DisableMissingPackagePluginsAsync(CancellationToken cancellationToken)
    {
        var packagesRoot = Path.GetFullPath(Path.Combine(_rootPath, "packages"));
        foreach (var pair in _entries.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray())
        {
            var packagePath = Path.GetFullPath(pair.Value.PackageDirectory);
            if (IsStrictlyWithin(packagePath, packagesRoot) && Directory.Exists(packagePath)) continue;
            var disabled = pair.Value with
            {
                IsEnabled = false,
                Health = pair.Value.Health with
                {
                    Status = "missing-package",
                    LastError = "The installed plugin package directory is missing or invalid."
                }
            };
            await repository.SaveAsync(disabled, cancellationToken);
            _entries[pair.Key] = disabled;
        }
    }

    private void CleanupUnreferencedPackages()
    {
        var packagesRoot = Path.GetFullPath(Path.Combine(_rootPath, "packages"));
        if (!Directory.Exists(packagesRoot)) return;
        var referenced = _entries.Values
            .Select(entry => Path.GetFullPath(entry.PackageDirectory))
            .Where(path => IsStrictlyWithin(path, packagesRoot))
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var pluginDirectory in Directory.EnumerateDirectories(packagesRoot))
        {
            foreach (var packageDirectory in Directory.EnumerateDirectories(pluginDirectory))
            {
                if (!referenced.Contains(Path.GetFullPath(packageDirectory)))
                    DeleteTransactionBestEffort(packageDirectory);
            }
            if (!Directory.EnumerateFileSystemEntries(pluginDirectory).Any())
                DeleteTransactionBestEffort(pluginDirectory);
        }
    }

    private void DeleteDirectoryWithinRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsStrictlyWithin(fullPath, _rootPath))
            throw new UnauthorizedAccessException("Plugin lifecycle cleanup path is outside the platform root.");
        if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
    }

    private string GetDataPath(string id) => Path.Combine(_rootPath, "data", id);

    private TimeSpan LifecycleWaitTimeout =>
        TimeSpan.FromMilliseconds(_options.InvocationTimeoutMilliseconds + 2_000);

    private static void EnsurePublisherContinuity(string? existingFingerprint, string? incomingFingerprint)
    {
        if (string.Equals(existingFingerprint, incomingFingerprint, StringComparison.Ordinal)) return;
        throw new UnauthorizedAccessException(
            "Plugin publisher identity does not match the installed or retained owner. Delete retained data before transferring ownership.");
    }

    private string? MoveToTransaction(string source, string destination)
    {
        if (!Directory.Exists(source)) return null;
        var fullSource = Path.GetFullPath(source);
        if (!IsWithin(fullSource, _rootPath))
            throw new UnauthorizedAccessException("Plugin lifecycle path is outside the plugin platform root.");
        Directory.Move(fullSource, destination);
        return destination;
    }

    private static void RestoreTransactionDirectory(string? backup, string? destination)
    {
        if (backup is null || destination is null || !Directory.Exists(backup)) return;
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(backup, destination);
    }

    private static void DeleteTransactionBestEffort(string? transactionPath)
    {
        if (transactionPath is null || !Directory.Exists(transactionPath)) return;
        try { Directory.Delete(transactionPath, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteTransaction(string? transactionPath)
    {
        if (transactionPath is not null && Directory.Exists(transactionPath))
            Directory.Delete(transactionPath, recursive: true);
    }

    private static string GetLifecycleJournalPath(string transactionPath)
        => $"{transactionPath}{LifecycleJournalSuffix}";

    private void DeleteLifecycleTransaction(string? transactionPath, string pluginId)
    {
        if (transactionPath is null) return;
        DeleteTransaction(transactionPath);
        var journalPath = GetLifecycleJournalPath(transactionPath);
        if (File.Exists(journalPath)) File.Delete(journalPath);
        if (!Directory.Exists(transactionPath) && !File.Exists(journalPath))
            _pendingLifecyclePluginIds.Remove(pluginId);
    }

    private void DeleteLifecycleTransactionBestEffort(string? transactionPath, string pluginId)
    {
        if (transactionPath is null) return;
        DeleteTransactionBestEffort(transactionPath);
        if (Directory.Exists(transactionPath)) return;
        try
        {
            var journalPath = GetLifecycleJournalPath(transactionPath);
            if (File.Exists(journalPath)) File.Delete(journalPath);
            if (!File.Exists(journalPath)) _pendingLifecyclePluginIds.Remove(pluginId);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static JsonElement ParseConfiguration(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), candidate);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private static bool IsStrictlyWithin(string candidate, string root)
        => !PathsEqual(candidate, root) && IsWithin(candidate, root);

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

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
