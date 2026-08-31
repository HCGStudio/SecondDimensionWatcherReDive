using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;

namespace SecondDimensionWatcherReDive.Configuration;

public interface IRuntimeSettingsInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

internal interface IRuntimeSettingsService
{
    Task<RuntimeSettingsState> GetAsync(CancellationToken cancellationToken);

    Task<RuntimeSettingsUpdateResult> UpdateAsync(
        RuntimeSettingsPatch patch,
        CancellationToken cancellationToken);
}

public sealed partial class RuntimeSettingsService : IRuntimeSettingsInitializer, IRuntimeSettingsService
{
    private const string ProtectorPurpose =
        "SecondDimensionWatcherReDive.RuntimeSettings.Secrets.v1";

    private static readonly JsonSerializerOptions StorageJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RuntimeSettingsConfigurationProvider _configurationProvider;
    private readonly IDataProtector _protector;
    private readonly ILogger<RuntimeSettingsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private RuntimeSettingsOverrides _persistedOverrides = new();
    private RuntimeSettingsOverrides _appliedOverrides = new();
    private RuntimeSecretOverrides _secretOverrides = new();
    private RuntimeSettingsValues? _runningValues;
    private int _allowedRootSlotCount;
    private long _revision;
    private bool _initialized;

    public RuntimeSettingsService(
        IServiceScopeFactory scopeFactory,
        RuntimeSettingsConfigurationProvider configurationProvider,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<RuntimeSettingsService> logger)
    {
        _scopeFactory = scopeFactory;
        _configurationProvider = configurationProvider;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _allowedRootSlotCount = DeploymentValues().MediaLibrary.AllowedRoots.Count;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IApplicationSettingsRepository>();
            var document = await repository.GetAsync(cancellationToken);

            if (document is not null)
            {
                _persistedOverrides = DeserializeOverrides(document.ValuesJson);
                _secretOverrides = UnprotectSecrets(document.ProtectedSecrets);
                _revision = document.Revision;
            }

            _appliedOverrides = _persistedOverrides;
            var deploymentValues = DeploymentValues();
            UpdateAllowedRootSlotCount(deploymentValues, _appliedOverrides);
            _runningValues = Merge(deploymentValues, _appliedOverrides);
            var resolvedSecrets = ResolveSecrets(_secretOverrides, DeploymentSecrets());
            var errors = RuntimeSettingsValidator.Validate(_runningValues, resolvedSecrets);
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    "Persisted runtime settings are invalid: " +
                    string.Join("; ", errors.SelectMany(pair =>
                        pair.Value.Select(value => $"{pair.Key}: {value}"))));

            PublishEffectiveConfiguration();
            _initialized = true;
            LogInitialized(_logger, _revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<RuntimeSettingsState> IRuntimeSettingsService.GetAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            await RefreshFromRepositoryUnderGateAsync(cancellationToken);
            return CreateState();
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<RuntimeSettingsUpdateResult> IRuntimeSettingsService.UpdateAsync(
        RuntimeSettingsPatch patch,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            await RefreshFromRepositoryUnderGateAsync(cancellationToken);

            if (patch.ExpectedRevision != _revision)
                return new RuntimeSettingsUpdateResult(
                    RuntimeSettingsUpdateStatus.Conflict,
                    CreateState(),
                    EmptyErrors());

            var mutationErrors = ValidateSecretMutations(patch);
            if (mutationErrors.Count > 0)
                return new RuntimeSettingsUpdateResult(
                    RuntimeSettingsUpdateStatus.Invalid,
                    CreateState(),
                    mutationErrors);

            var candidateOverrides = ApplyValues(_persistedOverrides, patch);
            var candidateSecrets = ApplySecrets(_secretOverrides, patch);
            var deploymentValues = DeploymentValues();
            var deploymentSecrets = DeploymentSecrets();
            var currentValues = Merge(deploymentValues, _persistedOverrides);
            var desiredValues = Merge(deploymentValues, candidateOverrides);
            candidateSecrets = PinEmptyCredentialsAcrossOriginChanges(
                candidateSecrets,
                currentValues,
                desiredValues,
                deploymentSecrets,
                patch);
            var resolvedSecrets = ResolveSecrets(candidateSecrets, deploymentSecrets);
            var validationErrors = MergeErrors(
                RuntimeSettingsValidator.Validate(desiredValues, resolvedSecrets),
                ValidateEndpointSecretChanges(
                    currentValues,
                    desiredValues,
                    resolvedSecrets,
                    patch));
            if (validationErrors.Count > 0)
                return new RuntimeSettingsUpdateResult(
                    RuntimeSettingsUpdateStatus.Invalid,
                    CreateState(),
                    validationErrors);

            var valuesJson = JsonSerializer.Serialize(candidateOverrides, StorageJsonOptions);
            var protectedSecrets = ProtectSecrets(candidateSecrets);
            var updatedAt = DateTimeOffset.UtcNow;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IApplicationSettingsRepository>();
            var saved = await repository.TrySaveAsync(
                valuesJson,
                protectedSecrets,
                patch.ExpectedRevision,
                updatedAt,
                cancellationToken);
            if (saved is null)
            {
                await ReloadAfterConflictAsync(repository, cancellationToken);
                return new RuntimeSettingsUpdateResult(
                    RuntimeSettingsUpdateStatus.Conflict,
                    CreateState(),
                    EmptyErrors());
            }

            _persistedOverrides = candidateOverrides;
            _secretOverrides = candidateSecrets;
            _revision = saved.Revision;

            // NFS owns a listener and lease state. Persist its desired value but keep the
            // running override unchanged until the next process initialization.
            _appliedOverrides = candidateOverrides with { Nfs = _appliedOverrides.Nfs };
            ApplyEffectiveConfiguration(preserveRunningNfs: true);

            LogUpdated(_logger, _revision, HasPendingRestart());
            return new RuntimeSettingsUpdateResult(
                RuntimeSettingsUpdateStatus.Saved,
                CreateState(),
                EmptyErrors());
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureInitialized();
            await RefreshFromRepositoryUnderGateAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshFromRepositoryUnderGateAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IApplicationSettingsRepository>();
        var document = await repository.GetAsync(cancellationToken);
        var databaseRevision = document?.Revision ?? 0;
        if (databaseRevision == _revision)
        {
            var previousSlotCount = _allowedRootSlotCount;
            UpdateAllowedRootSlotCount(DeploymentValues(), _appliedOverrides);
            if (previousSlotCount != _allowedRootSlotCount)
                PublishEffectiveConfiguration();
            return;
        }

        await ReloadAfterConflictAsync(repository, cancellationToken, document);
        LogSynchronized(_logger, _revision, HasPendingRestart());
    }

    private async Task ReloadAfterConflictAsync(
        IApplicationSettingsRepository repository,
        CancellationToken cancellationToken,
        Framework.DataRepository.ApplicationSettings? document = null)
    {
        document ??= await repository.GetAsync(cancellationToken);
        if (document is null)
        {
            _persistedOverrides = new();
            _secretOverrides = new();
            _revision = 0;
        }
        else
        {
            _persistedOverrides = DeserializeOverrides(document.ValuesJson);
            _secretOverrides = UnprotectSecrets(document.ProtectedSecrets);
            _revision = document.Revision;
        }

        // Other instances may hot-update all sections except NFS. Preserve this
        // process's NFS override while adopting their current hot settings.
        _appliedOverrides = _persistedOverrides with { Nfs = _appliedOverrides.Nfs };
        ApplyEffectiveConfiguration(preserveRunningNfs: true);
    }

    private void ApplyEffectiveConfiguration(bool preserveRunningNfs)
    {
        var deploymentValues = DeploymentValues();
        UpdateAllowedRootSlotCount(deploymentValues, _appliedOverrides);
        var merged = Merge(deploymentValues, _appliedOverrides);
        if (preserveRunningNfs && _runningValues is not null)
            merged = merged with { Nfs = _runningValues.Nfs };
        _runningValues = merged;
        PublishEffectiveConfiguration();
    }

    private void PublishEffectiveConfiguration()
    {
        if (_runningValues is null)
            return;

        _configurationProvider.Replace(
            RuntimeSettingsFlattener.Flatten(
                _runningValues,
                ResolveSecrets(_secretOverrides, DeploymentSecrets()),
                _allowedRootSlotCount));
    }

    private RuntimeSettingsValues DeploymentValues() =>
        RuntimeSettingsDefaults.FromConfiguration(_configurationProvider.DeploymentConfiguration);

    private IReadOnlyDictionary<string, string?> DeploymentSecrets() =>
        RuntimeSettingsDefaults.ReadDeploymentSecrets(_configurationProvider.DeploymentConfiguration);

    private RuntimeSettingsState CreateState()
    {
        var desired = Merge(DeploymentValues(), _persistedOverrides);
        return new RuntimeSettingsState(
            _revision,
            desired,
            ResolveSecrets(_secretOverrides, DeploymentSecrets()),
            HasPendingRestart(desired));
    }

    private bool HasPendingRestart(RuntimeSettingsValues? desired = null)
    {
        desired ??= Merge(DeploymentValues(), _persistedOverrides);
        return _runningValues is not null && !NfsValuesEqual(desired.Nfs, _runningValues.Nfs);
    }

    private static bool NfsValuesEqual(NfsSettingsValues left, NfsSettingsValues right)
    {
        return left.Enabled == right.Enabled
               && left.Port == right.Port
               && string.Equals(left.BindAddress.Trim(), right.BindAddress.Trim(), StringComparison.OrdinalIgnoreCase)
               && left.LeaseSeconds == right.LeaseSeconds
               && left.MaxConnections == right.MaxConnections
               && left.IdleTimeoutSeconds == right.IdleTimeoutSeconds
               && left.AllowAnonymous == right.AllowAnonymous
               && NormalizeNetworks(left.AllowedNetworks)
                   .SequenceEqual(NormalizeNetworks(right.AllowedNetworks), StringComparer.Ordinal);
    }

    private static IEnumerable<string> NormalizeNetworks(IEnumerable<string> networks) =>
        networks
            .Select(network => System.Net.IPNetwork.TryParse(network.Trim(), out var parsed)
                ? parsed.ToString()
                : network.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    private static RuntimeSettingsOverrides ApplyValues(
        RuntimeSettingsOverrides current,
        RuntimeSettingsPatch patch) =>
        current with
        {
            Ai = patch.Ai?.Values ?? current.Ai,
            Torrent = patch.Torrent?.Values ?? current.Torrent,
            MediaLibrary = patch.MediaLibrary ?? current.MediaLibrary,
            Incidents = patch.Incidents ?? current.Incidents,
            Nfs = patch.Nfs ?? current.Nfs
        };

    private static RuntimeSecretOverrides ApplySecrets(
        RuntimeSecretOverrides current,
        RuntimeSettingsPatch patch)
    {
        var values = new Dictionary<string, PersistedSecret>(current.Values, StringComparer.Ordinal);
        ApplySecret(values, RuntimeSecretKeys.OpenAiApiKey, patch.Ai?.OpenAiApiKey);
        ApplySecret(values, RuntimeSecretKeys.AnthropicApiKey, patch.Ai?.AnthropicApiKey);
        ApplySecret(values, RuntimeSecretKeys.CodexToken, patch.Ai?.CodexToken);
        ApplySecret(values, RuntimeSecretKeys.TmdbApiKey, patch.Tmdb?.ApiKey);
        ApplySecret(values, RuntimeSecretKeys.TorrentPassword, patch.Torrent?.Password);
        return new RuntimeSecretOverrides { Values = values };
    }

    private static RuntimeSecretOverrides PinEmptyCredentialsAcrossOriginChanges(
        RuntimeSecretOverrides candidate,
        RuntimeSettingsValues currentValues,
        RuntimeSettingsValues desiredValues,
        IReadOnlyDictionary<string, string?> deploymentSecrets,
        RuntimeSettingsPatch patch)
    {
        var values = new Dictionary<string, PersistedSecret>(candidate.Values, StringComparer.Ordinal);
        PinEmptyCredential(
            values,
            RuntimeSecretKeys.OpenAiApiKey,
            currentValues.Ai.OpenAI.BaseUrl,
            desiredValues.Ai.OpenAI.BaseUrl,
            deploymentSecrets,
            patch.Ai?.OpenAiApiKey);
        PinEmptyCredential(
            values,
            RuntimeSecretKeys.AnthropicApiKey,
            currentValues.Ai.Anthropic.BaseUrl,
            desiredValues.Ai.Anthropic.BaseUrl,
            deploymentSecrets,
            patch.Ai?.AnthropicApiKey);
        PinEmptyCredential(
            values,
            RuntimeSecretKeys.CodexToken,
            currentValues.Ai.CodexAppServer.Endpoint,
            desiredValues.Ai.CodexAppServer.Endpoint,
            deploymentSecrets,
            patch.Ai?.CodexToken);
        PinEmptyCredential(
            values,
            RuntimeSecretKeys.TorrentPassword,
            currentValues.Torrent.Url,
            desiredValues.Torrent.Url,
            deploymentSecrets,
            patch.Torrent?.Password);
        return new RuntimeSecretOverrides { Values = values };
    }

    private static void PinEmptyCredential(
        IDictionary<string, PersistedSecret> values,
        string key,
        string currentEndpoint,
        string candidateEndpoint,
        IReadOnlyDictionary<string, string?> deploymentSecrets,
        SecretMutation? mutation)
    {
        if (!OriginChanged(currentEndpoint, candidateEndpoint)
            || mutation?.Operation is SecretMutationOperation.Set or SecretMutationOperation.Clear
            || values.ContainsKey(key)
            || !string.IsNullOrEmpty(deploymentSecrets.GetValueOrDefault(key)))
            return;

        // Do not let a credential added to deployment configuration later silently cross the
        // newly selected origin. Reset remains available as an explicit future action.
        values[key] = new PersistedSecret(PersistedSecretMode.Clear, null);
    }

    private static void ApplySecret(
        IDictionary<string, PersistedSecret> values,
        string key,
        SecretMutation? mutation)
    {
        if (mutation is null || mutation.Operation == SecretMutationOperation.Keep)
            return;

        switch (mutation.Operation)
        {
            case SecretMutationOperation.Set:
                values[key] = new PersistedSecret(PersistedSecretMode.Set, mutation.Value);
                break;
            case SecretMutationOperation.Clear:
                values[key] = new PersistedSecret(PersistedSecretMode.Clear, null);
                break;
            case SecretMutationOperation.Reset:
                values.Remove(key);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation.Operation, null);
        }
    }

    private static IReadOnlyDictionary<string, string[]> ValidateSecretMutations(
        RuntimeSettingsPatch patch)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateSecretMutation(errors, "ai.openAI.apiKey", patch.Ai?.OpenAiApiKey);
        ValidateSecretMutation(errors, "ai.anthropic.apiKey", patch.Ai?.AnthropicApiKey);
        ValidateSecretMutation(errors, "ai.codexAppServer.token", patch.Ai?.CodexToken);
        ValidateSecretMutation(errors, "tmdb.apiKey", patch.Tmdb?.ApiKey);
        ValidateSecretMutation(errors, "torrent.password", patch.Torrent?.Password);
        return errors;
    }

    private static IReadOnlyDictionary<string, string[]> ValidateEndpointSecretChanges(
        RuntimeSettingsValues current,
        RuntimeSettingsValues candidate,
        IReadOnlyDictionary<string, ResolvedSecret> candidateSecrets,
        RuntimeSettingsPatch patch)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        RequireSecretRefreshForOriginChange(
            errors,
            "ai.openAI.apiKey",
            current.Ai.OpenAI.BaseUrl,
            candidate.Ai.OpenAI.BaseUrl,
            candidateSecrets[RuntimeSecretKeys.OpenAiApiKey],
            patch.Ai?.OpenAiApiKey);
        RequireSecretRefreshForOriginChange(
            errors,
            "ai.anthropic.apiKey",
            current.Ai.Anthropic.BaseUrl,
            candidate.Ai.Anthropic.BaseUrl,
            candidateSecrets[RuntimeSecretKeys.AnthropicApiKey],
            patch.Ai?.AnthropicApiKey);
        RequireSecretRefreshForOriginChange(
            errors,
            "ai.codexAppServer.token",
            current.Ai.CodexAppServer.Endpoint,
            candidate.Ai.CodexAppServer.Endpoint,
            candidateSecrets[RuntimeSecretKeys.CodexToken],
            patch.Ai?.CodexToken);
        RequireSecretRefreshForOriginChange(
            errors,
            "torrent.password",
            current.Torrent.Url,
            candidate.Torrent.Url,
            candidateSecrets[RuntimeSecretKeys.TorrentPassword],
            patch.Torrent?.Password);
        return errors;
    }

    private static void RequireSecretRefreshForOriginChange(
        IDictionary<string, string[]> errors,
        string secretPath,
        string currentEndpoint,
        string candidateEndpoint,
        ResolvedSecret candidateSecret,
        SecretMutation? mutation)
    {
        if (!candidateSecret.IsConfigured
            || !OriginChanged(currentEndpoint, candidateEndpoint)
            || mutation?.Operation is SecretMutationOperation.Set or SecretMutationOperation.Clear)
            return;

        errors[secretPath] =
        [
            "The endpoint origin changed. Set the credential again or explicitly clear it in the same request."
        ];
    }

    private static bool OriginChanged(string currentEndpoint, string candidateEndpoint)
    {
        if (!Uri.TryCreate(currentEndpoint, UriKind.Absolute, out var current)
            || !Uri.TryCreate(candidateEndpoint, UriKind.Absolute, out var candidate))
            return !string.Equals(
                currentEndpoint,
                candidateEndpoint,
                StringComparison.OrdinalIgnoreCase);

        return !string.Equals(
            current.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped),
            candidate.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string[]> MergeErrors(
        IReadOnlyDictionary<string, string[]> first,
        IReadOnlyDictionary<string, string[]> second)
    {
        if (first.Count == 0)
            return second;
        if (second.Count == 0)
            return first;

        var merged = first.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        foreach (var (key, values) in second)
        {
            merged[key] = merged.TryGetValue(key, out var existing)
                ? existing.Concat(values).ToArray()
                : values;
        }

        return merged;
    }

    private static void ValidateSecretMutation(
        IDictionary<string, string[]> errors,
        string path,
        SecretMutation? mutation)
    {
        if (mutation is null)
            return;

        if (!Enum.IsDefined(mutation.Operation))
        {
            errors[path] = ["The secret operation is invalid."];
            return;
        }

        if (mutation.Operation == SecretMutationOperation.Set)
        {
            if (string.IsNullOrWhiteSpace(mutation.Value))
                errors[path] = ["A non-empty value is required when setting a secret."];
            return;
        }

        if (!string.IsNullOrEmpty(mutation.Value))
            errors[path] = ["A value is only accepted for the set operation."];
    }

    private IReadOnlyDictionary<string, ResolvedSecret> ResolveSecrets(
        RuntimeSecretOverrides overrides,
        IReadOnlyDictionary<string, string?> deploymentSecrets)
    {
        var result = new Dictionary<string, ResolvedSecret>(StringComparer.Ordinal);
        foreach (var key in RuntimeSecretKeys.All)
        {
            if (overrides.Values.TryGetValue(key, out var persisted))
            {
                result[key] = persisted.Mode switch
                {
                    PersistedSecretMode.Set => new ResolvedSecret(
                        persisted.Value,
                        !string.IsNullOrEmpty(persisted.Value),
                        SecretConfigurationSource.Runtime),
                    PersistedSecretMode.Clear => new ResolvedSecret(
                        null,
                        false,
                        SecretConfigurationSource.Runtime),
                    _ => throw new ArgumentOutOfRangeException(nameof(persisted), persisted.Mode, null)
                };
                continue;
            }

            var deploymentValue = deploymentSecrets.GetValueOrDefault(key);
            result[key] = string.IsNullOrEmpty(deploymentValue)
                ? new ResolvedSecret(null, false, SecretConfigurationSource.None)
                : new ResolvedSecret(deploymentValue, true, SecretConfigurationSource.Deployment);
        }

        return result;
    }

    private string? ProtectSecrets(RuntimeSecretOverrides overrides)
    {
        if (overrides.Values.Count == 0)
            return null;

        var json = JsonSerializer.Serialize(overrides, StorageJsonOptions);
        return _protector.Protect(json);
    }

    private RuntimeSecretOverrides UnprotectSecrets(string? protectedSecrets)
    {
        if (string.IsNullOrEmpty(protectedSecrets))
            return new RuntimeSecretOverrides();

        try
        {
            var json = _protector.Unprotect(protectedSecrets);
            var result = JsonSerializer.Deserialize<RuntimeSecretOverrides>(json, StorageJsonOptions)
                         ?? throw new InvalidOperationException("The secret settings document is empty.");
            var unknownKeys = result.Values.Keys
                .Where(key => !RuntimeSecretKeys.All.Contains(key, StringComparer.Ordinal))
                .ToArray();
            if (unknownKeys.Length > 0)
                throw new InvalidOperationException(
                    "Runtime setting secrets contain unsupported keys: " + string.Join(", ", unknownKeys));
            return result with
            {
                Values = new Dictionary<string, PersistedSecret>(result.Values, StringComparer.Ordinal)
            };
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Runtime setting secrets could not be decrypted with the configured data-protection key ring.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Runtime setting secrets contain invalid JSON.", exception);
        }
    }

    private static RuntimeSettingsOverrides DeserializeOverrides(string json)
    {
        try
        {
            var overrides = JsonSerializer.Deserialize<RuntimeSettingsOverrides>(json, StorageJsonOptions)
                            ?? new();
            if (overrides.Ai is { CodexAppServer.PermissionProfile: null } ai)
            {
                overrides = overrides with
                {
                    Ai = ai with
                    {
                        CodexAppServer = ai.CodexAppServer with
                        {
                            PermissionProfile = ":read-only"
                        }
                    }
                };
            }

            return overrides;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Runtime settings contain invalid JSON.", exception);
        }
    }

    private static RuntimeSettingsValues Merge(
        RuntimeSettingsValues deployment,
        RuntimeSettingsOverrides overrides) =>
        new(
            overrides.Ai ?? deployment.Ai,
            overrides.Torrent ?? deployment.Torrent,
            overrides.MediaLibrary ?? deployment.MediaLibrary,
            overrides.Incidents ?? deployment.Incidents,
            overrides.Nfs ?? deployment.Nfs);

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Runtime settings have not been initialized. Call InitializeAsync after EF migrations and before starting the host.");
    }

    private void UpdateAllowedRootSlotCount(
        RuntimeSettingsValues deployment,
        RuntimeSettingsOverrides overrides)
    {
        _allowedRootSlotCount = Math.Max(
            _allowedRootSlotCount,
            deployment.MediaLibrary.AllowedRoots.Count);
        if (overrides.MediaLibrary is { } mediaLibrary)
            _allowedRootSlotCount = Math.Max(_allowedRootSlotCount, mediaLibrary.AllowedRoots.Count);
    }

    private static IReadOnlyDictionary<string, string[]> EmptyErrors() =>
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Runtime settings initialized at revision {Revision}.")]
    private static partial void LogInitialized(ILogger logger, long revision);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Runtime settings updated to revision {Revision}; pendingRestart={PendingRestart}.")]
    private static partial void LogUpdated(ILogger logger, long revision, bool pendingRestart);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Runtime settings synchronized to revision {Revision}; pendingRestart={PendingRestart}.")]
    private static partial void LogSynchronized(ILogger logger, long revision, bool pendingRestart);
}

internal sealed partial class RuntimeSettingsSynchronizationBackgroundService(
    RuntimeSettingsService settings,
    ILogger<RuntimeSettingsSynchronizationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, stoppingToken);
            try
            {
                await settings.SynchronizeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSynchronizationFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to synchronize runtime settings")]
    private static partial void LogSynchronizationFailed(ILogger logger, Exception exception);
}

public static class RuntimeSettingsServiceExtensions
{
    public static IServiceCollection AddApplicationRuntimeSettings(
        this IServiceCollection services,
        RuntimeSettingsConfigurationProvider configurationProvider)
    {
        services.AddDataProtection()
            .SetApplicationName("SecondDimensionWatcherReDive");
        services.TryAddScoped<IApplicationSettingsRepository, ApplicationSettingsRepository>();
        services.AddSingleton(configurationProvider);
        services.AddSingleton<RuntimeSettingsService>();
        services.AddSingleton<IRuntimeSettingsInitializer>(serviceProvider =>
            serviceProvider.GetRequiredService<RuntimeSettingsService>());
        services.AddSingleton<IRuntimeSettingsService>(serviceProvider =>
            serviceProvider.GetRequiredService<RuntimeSettingsService>());
        services.AddHostedService<RuntimeSettingsSynchronizationBackgroundService>();
        return services;
    }
}
