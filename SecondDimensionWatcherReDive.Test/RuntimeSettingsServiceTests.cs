using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class RuntimeSettingsServiceTests
{
    [TestMethod]
    public void SettingsController_RequiresJwtAuthentication_AndDisablesResponseCaching()
    {
        var authorize = typeof(SettingsController).GetCustomAttribute<AuthorizeAttribute>();
        var responseCache = typeof(SettingsController).GetCustomAttribute<ResponseCacheAttribute>();

        Assert.IsNotNull(authorize);
        Assert.AreEqual(JwtBearerDefaults.AuthenticationScheme, authorize.AuthenticationSchemes);
        Assert.IsNotNull(responseCache);
        Assert.IsTrue(responseCache.NoStore);
        Assert.AreEqual(ResponseCacheLocation.None, responseCache.Location);
    }

    [TestMethod]
    public async Task GetSettings_RedactsEverySecret()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var controller = new SettingsController(host.RuntimeSettings);

        var action = await controller.GetSettingsAsync(CancellationToken.None);
        var ok = action.Result as OkObjectResult;
        Assert.IsNotNull(ok);

        var json = JsonSerializer.Serialize(
            ok.Value,
            ok.Value!.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("deployment-openai-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-anthropic-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-codex-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-tmdb-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-torrent-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deployment-webhook-secret", json, StringComparison.Ordinal);
        StringAssert.Contains(json, "\"isConfigured\":true");
        StringAssert.Contains(json, "\"source\":\"deployment\"");
        StringAssert.Contains(json, "\"permissionProfile\":\":read-only\"");
    }

    [TestMethod]
    public async Task PatchSettings_ReturnsSecretMetadataWithoutEchoingNewValue()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var values = initial.Desired.Ai;
        const string NewSecret = "controller-must-not-echo-this-secret";
        var request = new PatchApplicationSettingsRequest(
            initial.Revision,
            new AiSettingsPatchRequest(
                values.ExecutionMode,
                values.Provider,
                new OpenAiSettingsPatchRequest(
                    values.OpenAI.BaseUrl,
                    values.OpenAI.ApiMode,
                    values.OpenAI.Model,
                    values.OpenAI.MaxTokens,
                    new SecretMutationRequest(SecretMutationOperation.Set, NewSecret)),
                new AnthropicSettingsPatchRequest(
                    values.Anthropic.BaseUrl,
                    values.Anthropic.Model,
                    values.Anthropic.MaxTokens,
                    values.Anthropic.ApiVersion,
                    ApiKey: null),
                new CodexAppServerSettingsPatchRequest(
                    values.CodexAppServer.Endpoint,
                    values.CodexAppServer.Model,
                    values.CodexAppServer.PermissionProfile,
                    values.CodexAppServer.TimeoutSeconds,
                    Token: null),
                new InferenceSettingsPatchRequest(values.Inference.RateLimitDelayMs)),
            Tmdb: null,
            Torrent: null,
            MediaLibrary: null,
            Incidents: null,
            Nfs: null);
        var controller = new SettingsController(host.RuntimeSettings);

        var response = await controller.PatchSettingsAsync(request, CancellationToken.None);

        var ok = response as OkObjectResult;
        Assert.IsNotNull(ok);
        var json = JsonSerializer.Serialize(
            ok.Value,
            ok.Value!.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(NewSecret, json, StringComparison.Ordinal);
        StringAssert.Contains(json, "\"isConfigured\":true");
        StringAssert.Contains(json, "\"source\":\"runtime\"");
    }

    [TestMethod]
    public async Task WebhookUrl_IsEncryptedAtRestAndNeverReturnedBySettingsApi()
    {
        await using var host = await SettingsTestHost.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                [RuntimeSecretKeys.NotificationWebhookUrl] = null
            });
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        const string WebhookUrl = "https://hooks.example.test/sdw?token=must-remain-secret";
        var result = await host.RuntimeSettings.UpdateAsync(
            new RuntimeSettingsPatch(
                initial.Revision,
                Ai: null,
                Tmdb: null,
                Torrent: null,
                MediaLibrary: null,
                Incidents: null,
                Nfs: null,
                Notifications: new NotificationSettingsUpdate(
                    initial.Desired.Notifications with { WebhookEnabled = true },
                    new SecretMutation(SecretMutationOperation.Set, WebhookUrl))),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, result.Status);
        Assert.AreEqual(WebhookUrl, host.Configuration[RuntimeSecretKeys.NotificationWebhookUrl]);
        Assert.DoesNotContain(
            WebhookUrl,
            host.Repository.Document?.ProtectedSecrets ?? string.Empty,
            StringComparison.Ordinal);

        var controller = new SettingsController(host.RuntimeSettings);
        var action = await controller.GetSettingsAsync(CancellationToken.None);
        var json = JsonSerializer.Serialize(
            ((OkObjectResult)action.Result!).Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(WebhookUrl, json, StringComparison.Ordinal);
        StringAssert.Contains(json, "\"webhookUrl\":{\"isConfigured\":true,\"source\":\"runtime\"}");
    }

    [TestMethod]
    public async Task SetSecret_EncryptsPersistence_AndPublishesRuntimeValue()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        const string NewSecret = "new-openai-secret-value";
        var patch = PatchAi(
            initial,
            initial.Desired.Ai,
            openAiApiKey: new SecretMutation(SecretMutationOperation.Set, NewSecret));

        var result = await host.RuntimeSettings.UpdateAsync(patch, CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, result.Status);
        Assert.AreEqual(1, result.State.Revision);
        var secret = result.State.Secrets[RuntimeSecretKeys.OpenAiApiKey];
        Assert.IsTrue(secret.IsConfigured);
        Assert.AreEqual(SecretConfigurationSource.Runtime, secret.Source);
        Assert.IsNotNull(host.Repository.Document);
        Assert.IsNotNull(host.Repository.Document.ProtectedSecrets);
        Assert.DoesNotContain(NewSecret, host.Repository.Document.ProtectedSecrets, StringComparison.Ordinal);
        Assert.DoesNotContain(NewSecret, host.Repository.Document.ValuesJson, StringComparison.Ordinal);
        Assert.AreEqual(NewSecret, host.Configuration[RuntimeSecretKeys.OpenAiApiKey]);
    }

    [TestMethod]
    public async Task ClearAndResetSecret_PreserveExplicitRuntimeSource_ThenRestoreDeploymentFallback()
    {
        await using var host = await SettingsTestHost.CreateAsync();

        var cleared = await host.RuntimeSettings.UpdateAsync(
            PatchTmdb(0, new SecretMutation(SecretMutationOperation.Clear, null)),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, cleared.Status);
        var clearedSecret = cleared.State.Secrets[RuntimeSecretKeys.TmdbApiKey];
        Assert.IsFalse(clearedSecret.IsConfigured);
        Assert.AreEqual(SecretConfigurationSource.Runtime, clearedSecret.Source);
        Assert.AreEqual(string.Empty, host.Configuration[RuntimeSecretKeys.TmdbApiKey]);

        var reset = await host.RuntimeSettings.UpdateAsync(
            PatchTmdb(1, new SecretMutation(SecretMutationOperation.Reset, null)),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, reset.Status);
        var resetSecret = reset.State.Secrets[RuntimeSecretKeys.TmdbApiKey];
        Assert.IsTrue(resetSecret.IsConfigured);
        Assert.AreEqual(SecretConfigurationSource.Deployment, resetSecret.Source);
        Assert.AreEqual("deployment-tmdb-secret", host.Configuration[RuntimeSecretKeys.TmdbApiKey]);
        Assert.AreEqual(
            "deployment-tmdb-secret",
            host.Provider.GetSnapshot()[RuntimeSecretKeys.TmdbApiKey]);
    }

    [TestMethod]
    public async Task RemotePlainWebSocketEndpoint_IsRejected()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var ai = initial.Desired.Ai with
        {
            ExecutionMode = AiExecutionMode.CodexAppServer,
            CodexAppServer = initial.Desired.Ai.CodexAppServer with
            {
                Endpoint = "ws://agent.example.test/app-server"
            }
        };

        var result = await host.RuntimeSettings.UpdateAsync(
            PatchAi(
                initial,
                ai,
                codexToken: new SecretMutation(SecretMutationOperation.Clear, null)),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Invalid, result.Status);
        Assert.IsTrue(result.Errors.ContainsKey("ai.codexAppServer.endpoint"));
        StringAssert.Contains(
            result.Errors["ai.codexAppServer.endpoint"][0],
            "loopback");
        Assert.AreEqual(0, host.Repository.SaveCalls);
    }

    [TestMethod]
    public async Task EmptyCodexPermissionProfile_IsRejected()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var ai = initial.Desired.Ai with
        {
            CodexAppServer = initial.Desired.Ai.CodexAppServer with
            {
                PermissionProfile = " "
            }
        };

        var result = await host.RuntimeSettings.UpdateAsync(
            PatchAi(initial, ai),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Invalid, result.Status);
        Assert.IsTrue(result.Errors.ContainsKey("ai.codexAppServer.permissionProfile"));
        Assert.AreEqual(0, host.Repository.SaveCalls);
    }

    [TestMethod]
    public async Task MissingCodexPermissionProfile_UsesReadOnlyDefault()
    {
        await using var host = await SettingsTestHost.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["AI:CodexAppServer:PermissionProfile"] = null
            });

        var state = await host.RuntimeSettings.GetAsync(CancellationToken.None);

        Assert.AreEqual(":read-only", state.Desired.Ai.CodexAppServer.PermissionProfile);
        Assert.AreEqual(":read-only", host.Configuration["AI:CodexAppServer:PermissionProfile"]);
    }

    [TestMethod]
    [DataRow("AI:Engine", "builtInn", "ai.executionMode")]
    [DataRow("AI:Provider", "anthropicc", "ai.provider")]
    [DataRow("AI:OpenAI:ApiMode", "responsez", "ai.openAI.apiMode")]
    public async Task InvalidDeploymentAiEnum_FailsInitialization(
        string configurationKey,
        string invalidValue,
        string errorPath)
    {
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            SettingsTestHost.CreateAsync(
                configurationOverrides: new Dictionary<string, string?>
                {
                    [configurationKey] = invalidValue
                }));

        StringAssert.Contains(exception.Message, errorPath);
    }

    [TestMethod]
    public async Task MissingDeploymentAiEnums_UseDefaults()
    {
        await using var host = await SettingsTestHost.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["AI:Engine"] = null,
                ["AI:Provider"] = null,
                ["AI:OpenAI:ApiMode"] = null
            });

        var state = await host.RuntimeSettings.GetAsync(CancellationToken.None);

        Assert.AreEqual(AiExecutionMode.BuiltIn, state.Desired.Ai.ExecutionMode);
        Assert.AreEqual(BuiltInAiProvider.OpenAI, state.Desired.Ai.Provider);
        Assert.AreEqual(OpenAiApiMode.ChatCompletions, state.Desired.Ai.OpenAI.ApiMode);
    }

    [TestMethod]
    public async Task ChangingCredentialedOrigins_RequiresSetOrClearInSamePatch()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var ai = initial.Desired.Ai with
        {
            OpenAI = initial.Desired.Ai.OpenAI with { BaseUrl = "https://openai-proxy.example/v1" },
            Anthropic = initial.Desired.Ai.Anthropic with { BaseUrl = "https://anthropic-proxy.example" },
            CodexAppServer = initial.Desired.Ai.CodexAppServer with
            {
                Endpoint = "wss://codex.example/app-server"
            }
        };

        var rejected = await host.RuntimeSettings.UpdateAsync(
            PatchAi(initial, ai),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Invalid, rejected.Status);
        Assert.IsTrue(rejected.Errors.ContainsKey("ai.openAI.apiKey"));
        Assert.IsTrue(rejected.Errors.ContainsKey("ai.anthropic.apiKey"));
        Assert.IsTrue(rejected.Errors.ContainsKey("ai.codexAppServer.token"));
        Assert.AreEqual(0, host.Repository.SaveCalls);

        var accepted = await host.RuntimeSettings.UpdateAsync(
            PatchAi(
                initial,
                ai,
                openAiApiKey: new SecretMutation(SecretMutationOperation.Set, "new-openai"),
                anthropicApiKey: new SecretMutation(SecretMutationOperation.Clear, null),
                codexToken: new SecretMutation(SecretMutationOperation.Set, "new-codex")),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, accepted.Status);
        Assert.AreEqual(1, host.Repository.SaveCalls);
    }

    [TestMethod]
    public async Task ChangingTorrentOrigin_RequiresSetOrClearInSamePatch()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var torrent = initial.Desired.Torrent with
        {
            Url = "https://torrent.example.test"
        };

        var rejected = await host.RuntimeSettings.UpdateAsync(
            new RuntimeSettingsPatch(
                initial.Revision,
                Ai: null,
                Tmdb: null,
                Torrent: new TorrentSettingsUpdate(torrent, Password: null),
                MediaLibrary: null,
                Incidents: null,
                Nfs: null),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Invalid, rejected.Status);
        Assert.IsTrue(rejected.Errors.ContainsKey("torrent.password"));
        Assert.AreEqual(0, host.Repository.SaveCalls);

        var accepted = await host.RuntimeSettings.UpdateAsync(
            new RuntimeSettingsPatch(
                initial.Revision,
                Ai: null,
                Tmdb: null,
                Torrent: new TorrentSettingsUpdate(
                    torrent,
                    new SecretMutation(SecretMutationOperation.Clear, null)),
                MediaLibrary: null,
                Incidents: null,
                Nfs: null),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, accepted.Status);
        Assert.AreEqual(1, host.Repository.SaveCalls);
    }

    [TestMethod]
    public async Task InvalidTorrentUserAgent_IsRejectedBeforePersistence()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var torrent = initial.Desired.Torrent with
        {
            UserAgent = "SecondDimensionWatcher/1.0\nInjected: true"
        };

        var result = await host.RuntimeSettings.UpdateAsync(
            new RuntimeSettingsPatch(
                initial.Revision,
                Ai: null,
                Tmdb: null,
                Torrent: new TorrentSettingsUpdate(torrent, Password: null),
                MediaLibrary: null,
                Incidents: null,
                Nfs: null),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Invalid, result.Status);
        Assert.IsTrue(result.Errors.ContainsKey("torrent.userAgent"));
        Assert.AreEqual(0, host.Repository.SaveCalls);
    }

    [TestMethod]
    public async Task AddingFirstCodexEndpoint_RequiresCredentialRefreshWhenDeploymentTokenExists()
    {
        await using var host = await SettingsTestHost.CreateAsync(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["AI:CodexAppServer:Endpoint"] = string.Empty
            });
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var ai = initial.Desired.Ai with
        {
            CodexAppServer = initial.Desired.Ai.CodexAppServer with
            {
                Endpoint = "wss://codex.example.test/app-server"
            }
        };

        var rejected = await host.RuntimeSettings.UpdateAsync(
            PatchAi(initial, ai),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Invalid, rejected.Status);
        Assert.IsTrue(rejected.Errors.ContainsKey("ai.codexAppServer.token"));
        Assert.AreEqual(0, host.Repository.SaveCalls);

        var accepted = await host.RuntimeSettings.UpdateAsync(
            PatchAi(
                initial,
                ai,
                codexToken: new SecretMutation(SecretMutationOperation.Set, "new-codex-token")),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, accepted.Status);
    }

    [TestMethod]
    public async Task ShorteningAllowedRoots_WritesNullTombstonesForLowerProviderIndices()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var reloads = 0;
        using var registration = ChangeToken.OnChange(
            host.Configuration.GetReloadToken,
            () => Interlocked.Increment(ref reloads));
        var initial = await host.RuntimeSettings.GetAsync(CancellationToken.None);
        var mediaLibrary = initial.Desired.MediaLibrary with
        {
            AllowedRoots = ["/runtime/only"]
        };

        var result = await host.RuntimeSettings.UpdateAsync(
            new RuntimeSettingsPatch(
                0,
                Ai: null,
                Tmdb: null,
                Torrent: null,
                MediaLibrary: mediaLibrary,
                Incidents: null,
                Nfs: null),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, result.Status);
        var snapshot = host.Provider.GetSnapshot();
        Assert.AreEqual("/runtime/only", snapshot["MediaLibrary:AllowedRoots:0"]);
        Assert.IsTrue(snapshot.ContainsKey("MediaLibrary:AllowedRoots:1"));
        Assert.IsNull(snapshot["MediaLibrary:AllowedRoots:1"]);
        Assert.IsTrue(snapshot.ContainsKey("MediaLibrary:AllowedRoots:2"));
        Assert.IsNull(snapshot["MediaLibrary:AllowedRoots:2"]);
        Assert.IsNull(host.Configuration["MediaLibrary:AllowedRoots:1"]);
        Assert.IsNull(host.Configuration["MediaLibrary:AllowedRoots:2"]);
        var rebound = host.Configuration
            .GetSection("MediaLibrary:AllowedRoots")
            .Get<string[]>() ?? [];
        CollectionAssert.DoesNotContain(rebound, "/deployment/two");
        CollectionAssert.DoesNotContain(rebound, "/deployment/three");
        Assert.IsGreaterThanOrEqualTo(1, Volatile.Read(ref reloads));
    }

    [TestMethod]
    public async Task NfsChange_IsPersistedButNotPublishedUntilNextInitialization()
    {
        var repository = new FakeApplicationSettingsRepository();
        var dataProtection = new EphemeralDataProtectionProvider();
        await using (var first = await SettingsTestHost.CreateAsync(repository, dataProtection))
        {
            var initial = await first.RuntimeSettings.GetAsync(CancellationToken.None);
            var nfs = initial.Desired.Nfs with { Enabled = true, Port = 2050 };

            var saved = await first.RuntimeSettings.UpdateAsync(
                new RuntimeSettingsPatch(
                    0,
                    Ai: null,
                    Tmdb: null,
                    Torrent: null,
                    MediaLibrary: null,
                    Incidents: null,
                    Nfs: nfs),
                CancellationToken.None);

            Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, saved.Status);
            Assert.IsTrue(saved.State.PendingRestart);
            Assert.IsTrue(saved.State.Desired.Nfs.Enabled);
            Assert.AreEqual("false", first.Configuration["Nfs:Enabled"]?.ToLowerInvariant());
            Assert.AreEqual("False", first.Provider.GetSnapshot()["Nfs:Enabled"]);
        }

        await using var restarted = await SettingsTestHost.CreateAsync(repository, dataProtection);
        var restartedState = await restarted.RuntimeSettings.GetAsync(CancellationToken.None);
        Assert.IsFalse(restartedState.PendingRestart);
        Assert.IsTrue(restartedState.Desired.Nfs.Enabled);
        Assert.AreEqual("true", restarted.Configuration["Nfs:Enabled"]?.ToLowerInvariant());
        Assert.AreEqual("2050", restarted.Configuration["Nfs:Port"]);
    }

    [TestMethod]
    public async Task StaleRevision_ReturnsConflictWithoutPersisting()
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var controller = new SettingsController(host.RuntimeSettings);
        var request = new PatchApplicationSettingsRequest(
            ExpectedRevision: 99,
            Ai: null,
            Tmdb: new TmdbSettingsPatchRequest(null),
            Torrent: null,
            MediaLibrary: null,
            Incidents: null,
            Nfs: null);

        var response = await controller.PatchSettingsAsync(request, CancellationToken.None);

        Assert.IsInstanceOfType<ConflictObjectResult>(response);
        Assert.AreEqual(0, host.Repository.SaveCalls);
    }

    [TestMethod]
    public async Task ConcurrentServices_RejectStaleDatabaseRevision_AndReloadWinningState()
    {
        var repository = new FakeApplicationSettingsRepository();
        var dataProtection = new EphemeralDataProtectionProvider();
        await using var first = await SettingsTestHost.CreateAsync(repository, dataProtection);
        await using var second = await SettingsTestHost.CreateAsync(repository, dataProtection);

        var winner = await first.RuntimeSettings.UpdateAsync(
            PatchTmdb(0, new SecretMutation(SecretMutationOperation.Clear, null)),
            CancellationToken.None);
        var stale = await second.RuntimeSettings.UpdateAsync(
            PatchTmdb(0, new SecretMutation(SecretMutationOperation.Set, "must-not-be-saved")),
            CancellationToken.None);

        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, winner.Status);
        Assert.AreEqual(RuntimeSettingsUpdateStatus.Conflict, stale.Status);
        Assert.AreEqual(1, stale.State.Revision);
        var reloadedSecret = stale.State.Secrets[RuntimeSecretKeys.TmdbApiKey];
        Assert.IsFalse(reloadedSecret.IsConfigured);
        Assert.AreEqual(SecretConfigurationSource.Runtime, reloadedSecret.Source);
        Assert.AreEqual(string.Empty, second.Configuration[RuntimeSecretKeys.TmdbApiKey]);
        Assert.DoesNotContain(
            "must-not-be-saved",
            repository.Document?.ProtectedSecrets ?? string.Empty,
            StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(
        "AI:OpenAI:BaseUrl",
        "https://new-openai.example.test/v1",
        RuntimeSecretKeys.OpenAiApiKey)]
    [DataRow(
        "AI:CodexAppServer:Endpoint",
        "wss://new-codex.example.test/app-server",
        RuntimeSecretKeys.CodexToken)]
    [DataRow(
        "Torrent:Remote:Url",
        "https://new-qbit.example.test/",
        RuntimeSecretKeys.TorrentPassword)]
    public async Task DeploymentReload_ChangingCredentialedOriginAlone_DoesNotPublishOldSecretAtNewOrigin(
        string endpointKey,
        string newEndpoint,
        string secretKey)
    {
        await using var host = await SettingsTestHost.CreateAsync();
        var oldSecret = host.Configuration[secretKey];
        Assert.IsFalse(string.IsNullOrWhiteSpace(oldSecret));

        host.ReloadDeployment((endpointKey, newEndpoint));
        await host.Service.SynchronizeAsync(CancellationToken.None);

        Assert.IsFalse(
            string.Equals(host.Configuration[endpointKey], newEndpoint, StringComparison.Ordinal)
            && string.Equals(host.Configuration[secretKey], oldSecret, StringComparison.Ordinal),
            "A deployment reload must not combine a newly selected origin with the credential from the previous origin.");
    }

    [TestMethod]
    public async Task DeploymentReload_AddingAllowedRootBeyondStartupSnapshot_DoesNotExpandEffectiveRoots()
    {
        await using var host = await SettingsTestHost.CreateAsync();

        host.ReloadDeployment(("MediaLibrary:AllowedRoots:3", "/deployment/four"));
        await host.Service.SynchronizeAsync(CancellationToken.None);

        var effectiveRoots = host.Configuration
            .GetSection("MediaLibrary:AllowedRoots")
            .Get<string[]>() ?? [];
        CollectionAssert.DoesNotContain(effectiveRoots, "/deployment/four");
    }

    [TestMethod]
    public async Task ClearTombstone_SynchronizedFromAnotherInstance_BlocksFutureDeploymentSecret()
    {
        var repository = new FakeApplicationSettingsRepository();
        var dataProtection = new EphemeralDataProtectionProvider();
        await using var first = await SettingsTestHost.CreateAsync(repository, dataProtection);
        await using var second = await SettingsTestHost.CreateAsync(repository, dataProtection);

        var cleared = await first.RuntimeSettings.UpdateAsync(
            PatchTmdb(0, new SecretMutation(SecretMutationOperation.Clear, null)),
            CancellationToken.None);
        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, cleared.Status);

        await second.Service.SynchronizeAsync(CancellationToken.None);
        Assert.AreEqual(string.Empty, second.Configuration[RuntimeSecretKeys.TmdbApiKey]);

        second.ReloadDeployment((RuntimeSecretKeys.TmdbApiKey, "future-deployment-tmdb-secret"));
        await second.Service.SynchronizeAsync(CancellationToken.None);

        var synchronized = await second.RuntimeSettings.GetAsync(CancellationToken.None);
        var secret = synchronized.Secrets[RuntimeSecretKeys.TmdbApiKey];
        Assert.IsFalse(secret.IsConfigured);
        Assert.AreEqual(SecretConfigurationSource.Runtime, secret.Source);
        Assert.AreEqual(string.Empty, second.Configuration[RuntimeSecretKeys.TmdbApiKey]);
    }

    [TestMethod]
    public async Task OriginChangeWithoutCredential_PersistsClearTombstoneAgainstFutureDeploymentSecret()
    {
        const string NewOrigin = "https://new-openai.example.test/v1";
        var repository = new FakeApplicationSettingsRepository();
        var dataProtection = new EphemeralDataProtectionProvider();
        var withoutOpenAiSecret = new Dictionary<string, string?>
        {
            [RuntimeSecretKeys.OpenAiApiKey] = null
        };
        await using var first = await SettingsTestHost.CreateAsync(
            repository,
            dataProtection,
            withoutOpenAiSecret);
        await using var second = await SettingsTestHost.CreateAsync(
            repository,
            dataProtection,
            withoutOpenAiSecret);
        var initial = await first.RuntimeSettings.GetAsync(CancellationToken.None);
        var ai = initial.Desired.Ai with
        {
            OpenAI = initial.Desired.Ai.OpenAI with { BaseUrl = NewOrigin }
        };

        var saved = await first.RuntimeSettings.UpdateAsync(
            PatchAi(initial, ai),
            CancellationToken.None);
        Assert.AreEqual(RuntimeSettingsUpdateStatus.Saved, saved.Status);

        await second.Service.SynchronizeAsync(CancellationToken.None);
        second.ReloadDeployment((RuntimeSecretKeys.OpenAiApiKey, "future-deployment-openai-secret"));
        await second.Service.SynchronizeAsync(CancellationToken.None);

        var synchronized = await second.RuntimeSettings.GetAsync(CancellationToken.None);
        var secret = synchronized.Secrets[RuntimeSecretKeys.OpenAiApiKey];
        Assert.AreEqual(NewOrigin, second.Configuration["AI:OpenAI:BaseUrl"]);
        Assert.IsFalse(secret.IsConfigured);
        Assert.AreEqual(SecretConfigurationSource.Runtime, secret.Source);
        Assert.AreEqual(string.Empty, second.Configuration[RuntimeSecretKeys.OpenAiApiKey]);
    }

    private static RuntimeSettingsPatch PatchAi(
        RuntimeSettingsState state,
        AiSettingsValues values,
        SecretMutation? openAiApiKey = null,
        SecretMutation? anthropicApiKey = null,
        SecretMutation? codexToken = null) =>
        new(
            state.Revision,
            new AiSettingsUpdate(values, openAiApiKey, anthropicApiKey, codexToken),
            Tmdb: null,
            Torrent: null,
            MediaLibrary: null,
            Incidents: null,
            Nfs: null);

    private static RuntimeSettingsPatch PatchTmdb(long revision, SecretMutation mutation) =>
        new(
            revision,
            Ai: null,
            new TmdbSettingsUpdate(mutation),
            Torrent: null,
            MediaLibrary: null,
            Incidents: null,
            Nfs: null);

    private sealed class SettingsTestHost : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private SettingsTestHost(
            ServiceProvider services,
            RuntimeSettingsService service,
            IConfigurationRoot configuration,
            RuntimeSettingsConfigurationProvider provider,
            ReloadableConfigurationProvider deploymentProvider,
            FakeApplicationSettingsRepository repository)
        {
            _services = services;
            Service = service;
            Configuration = configuration;
            Provider = provider;
            DeploymentProvider = deploymentProvider;
            Repository = repository;
        }

        public RuntimeSettingsService Service { get; }

        public IRuntimeSettingsService RuntimeSettings => Service;

        public IConfigurationRoot Configuration { get; }

        public RuntimeSettingsConfigurationProvider Provider { get; }

        private ReloadableConfigurationProvider DeploymentProvider { get; }

        public FakeApplicationSettingsRepository Repository { get; }

        public static async Task<SettingsTestHost> CreateAsync(
            FakeApplicationSettingsRepository? repository = null,
            IDataProtectionProvider? dataProtectionProvider = null,
            IReadOnlyDictionary<string, string?>? configurationOverrides = null)
        {
            repository ??= new FakeApplicationSettingsRepository();
            dataProtectionProvider ??= new EphemeralDataProtectionProvider();

            var deploymentConfiguration = DeploymentConfiguration();
            if (configurationOverrides is not null)
            {
                foreach (var (key, value) in configurationOverrides)
                    deploymentConfiguration[key] = value;
            }

            var deploymentProvider = new ReloadableConfigurationProvider(deploymentConfiguration);
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ReloadableConfigurationSource(deploymentProvider));
            var runtimeProvider = configurationBuilder.AddRuntimeSettingsConfigurationProvider();
            var configuration = configurationBuilder.Build();

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IApplicationSettingsRepository>(repository)
                .BuildServiceProvider();
            var service = new RuntimeSettingsService(
                services.GetRequiredService<IServiceScopeFactory>(),
                runtimeProvider,
                dataProtectionProvider,
                NullLogger<RuntimeSettingsService>.Instance);
            await service.InitializeAsync(CancellationToken.None);
            return new SettingsTestHost(
                services,
                service,
                configuration,
                runtimeProvider,
                deploymentProvider,
                repository);
        }

        public void ReloadDeployment(params (string Key, string? Value)[] changes) =>
            DeploymentProvider.ReplaceAndReload(changes);

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
        }

        private static Dictionary<string, string?> DeploymentConfiguration() =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AI:Engine"] = "BuiltIn",
                ["AI:Provider"] = "OpenAI",
                ["AI:OpenAI:BaseUrl"] = "https://api.openai.com/v1",
                ["AI:OpenAI:ApiMode"] = "Responses",
                ["AI:OpenAI:Model"] = "gpt-test",
                ["AI:OpenAI:MaxTokens"] = "1024",
                ["AI:OpenAI:ApiKey"] = "deployment-openai-secret",
                ["AI:Anthropic:BaseUrl"] = "https://api.anthropic.com",
                ["AI:Anthropic:Model"] = "claude-test",
                ["AI:Anthropic:MaxTokens"] = "1024",
                ["AI:Anthropic:ApiVersion"] = "2023-06-01",
                ["AI:Anthropic:ApiKey"] = "deployment-anthropic-secret",
                ["AI:CodexAppServer:Endpoint"] = "ws://127.0.0.1:4500/app-server",
                ["AI:CodexAppServer:BearerToken"] = "deployment-codex-secret",
                ["AI:CodexAppServer:Model"] = "codex-test",
                ["AI:CodexAppServer:PermissionProfile"] = ":read-only",
                ["AI:CodexAppServer:TimeoutSeconds"] = "300",
                ["Inference:RateLimitDelayMs"] = "1000",
                ["TmdbApiKey"] = "deployment-tmdb-secret",
                ["Torrent:Remote:Url"] = "http://127.0.0.1:8080",
                ["Torrent:Remote:UserName"] = "admin",
                ["Torrent:Remote:Password"] = "deployment-torrent-secret",
                ["MediaLibrary:AllowedRoots:0"] = "/deployment/one",
                ["MediaLibrary:AllowedRoots:1"] = "/deployment/two",
                ["MediaLibrary:AllowedRoots:2"] = "/deployment/three",
                ["MediaLibrary:ScanInterval"] = "00:05:00",
                ["MediaLibrary:SettlingPeriod"] = "00:00:30",
                ["MediaLibrary:MissingGracePeriod"] = "1.00:00:00",
                ["Incidents:DownloadStalledAfter"] = "00:15:00",
                ["Incidents:ReportThrottle"] = "00:05:00",
                ["Incidents:ReconciliationInterval"] = "00:05:00",
                ["Incidents:Disk:MinimumAvailableBytes"] = "5368709120",
                ["Incidents:Disk:MinimumAvailablePercent"] = "5",
                ["Notifications:Webhook:Enabled"] = "false",
                ["Notifications:Webhook:Url"] = "https://hooks.example.test/delivery?token=deployment-webhook-secret",
                ["Notifications:Events"] = "ReleaseMatched,DownloadCompleted",
                ["Notifications:QuietHours:TimeZone"] = "UTC",
                ["Nfs:Enabled"] = "false",
                ["Nfs:Port"] = "2049",
                ["Nfs:BindAddress"] = "127.0.0.1",
                ["Nfs:LeaseSeconds"] = "90",
                ["Nfs:MaxConnections"] = "32"
            };
    }

    private sealed class ReloadableConfigurationSource(ReloadableConfigurationProvider provider)
        : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private sealed class ReloadableConfigurationProvider(
        IReadOnlyDictionary<string, string?> initialValues) : ConfigurationProvider
    {
        public override void Load() =>
            Data = new Dictionary<string, string?>(initialValues, StringComparer.OrdinalIgnoreCase);

        public void ReplaceAndReload(IEnumerable<(string Key, string? Value)> changes)
        {
            var replacement = new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in changes)
                replacement[key] = value;
            Data = replacement;
            OnReload();
        }
    }

    private sealed class FakeApplicationSettingsRepository : IApplicationSettingsRepository
    {
        private readonly object _gate = new();

        public ApplicationSettings? Document { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<ApplicationSettings?> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(Document);
            }
        }

        public Task<ApplicationSettings?> TrySaveAsync(
            string valuesJson,
            string? protectedSecrets,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                SaveCalls++;
                var currentRevision = Document?.Revision ?? 0;
                if (expectedRevision != currentRevision)
                    return Task.FromResult<ApplicationSettings?>(null);

                Document = new ApplicationSettings(
                    Models.ApplicationSettings.SingletonId,
                    valuesJson,
                    protectedSecrets,
                    checked(currentRevision + 1),
                    updatedAt);
                return Task.FromResult<ApplicationSettings?>(Document);
            }
        }
    }
}
