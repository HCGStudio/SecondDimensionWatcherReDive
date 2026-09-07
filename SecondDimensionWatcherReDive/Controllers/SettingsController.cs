using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Controllers.External;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
internal sealed class SettingsController(IRuntimeSettingsService settingsService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApplicationSettingsResponse>> GetSettingsAsync(
        CancellationToken cancellationToken)
    {
        var state = await settingsService.GetAsync(cancellationToken);
        return Ok(ToResponse(state));
    }

    [HttpPatch]
    public async Task<IActionResult> PatchSettingsAsync(
        [FromBody] PatchApplicationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Ai is null
            && request.Tmdb is null
            && request.Torrent is null
            && request.MediaLibrary is null
            && request.Incidents is null
            && request.Nfs is null
            && request.Notifications is null)
        {
            ModelState.AddModelError(string.Empty, "At least one settings section is required.");
            return ValidationProblem(ModelState);
        }

        if (!TryMapPatch(request, out var patch))
            return ValidationProblem(ModelState);

        var result = await settingsService.UpdateAsync(patch, cancellationToken);
        return result.Status switch
        {
            RuntimeSettingsUpdateStatus.Saved => Ok(ToResponse(result.State)),
            RuntimeSettingsUpdateStatus.Conflict => Conflict(ToResponse(result.State)),
            RuntimeSettingsUpdateStatus.Invalid => ValidationProblem(
                new ValidationProblemDetails(result.Errors.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal))),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Status), result.Status, null)
        };
    }

    private bool TryMapPatch(
        PatchApplicationSettingsRequest request,
        out RuntimeSettingsPatch patch)
    {
        var ai = MapAi(request.Ai);
        var torrent = MapTorrent(request.Torrent);
        var mediaLibrary = MapMediaLibrary(request.MediaLibrary);
        var incidents = MapIncidents(request.Incidents);
        var nfs = MapNfs(request.Nfs);
        var notifications = MapNotifications(request.Notifications);

        patch = new RuntimeSettingsPatch(
            request.ExpectedRevision,
            ai,
            request.Tmdb is null
                ? null
                : new TmdbSettingsUpdate(MapSecret(request.Tmdb.ApiKey, "tmdb.apiKey")),
            torrent,
            mediaLibrary,
            incidents,
            nfs,
            notifications);
        return ModelState.IsValid;
    }

    private AiSettingsUpdate? MapAi(AiSettingsPatchRequest? request)
    {
        if (request is null)
            return null;

        if (request.ExecutionMode is null) AddRequired("ai.executionMode");
        if (request.Provider is null) AddRequired("ai.provider");
        if (request.OpenAI is null) AddRequired("ai.openAI");
        if (request.Anthropic is null) AddRequired("ai.anthropic");
        if (request.CodexAppServer is null) AddRequired("ai.codexAppServer");
        if (request.Inference is null) AddRequired("ai.inference");
        if (!ModelState.IsValid)
            return null;

        var openAi = request.OpenAI!;
        var anthropic = request.Anthropic!;
        var codex = request.CodexAppServer!;
        var inference = request.Inference!;
        if (openAi.BaseUrl is null) AddRequired("ai.openAI.baseUrl");
        if (openAi.ApiMode is null) AddRequired("ai.openAI.apiMode");
        if (openAi.Model is null) AddRequired("ai.openAI.model");
        if (openAi.MaxTokens is null) AddRequired("ai.openAI.maxTokens");
        if (anthropic.BaseUrl is null) AddRequired("ai.anthropic.baseUrl");
        if (anthropic.Model is null) AddRequired("ai.anthropic.model");
        if (anthropic.MaxTokens is null) AddRequired("ai.anthropic.maxTokens");
        if (anthropic.ApiVersion is null) AddRequired("ai.anthropic.apiVersion");
        if (codex.Endpoint is null) AddRequired("ai.codexAppServer.endpoint");
        if (codex.PermissionProfile is null) AddRequired("ai.codexAppServer.permissionProfile");
        if (codex.TimeoutSeconds is null) AddRequired("ai.codexAppServer.timeoutSeconds");
        if (inference.RateLimitDelayMs is null) AddRequired("ai.inference.rateLimitDelayMs");
        if (!ModelState.IsValid)
            return null;

        return new AiSettingsUpdate(
            new AiSettingsValues(
                request.ExecutionMode!.Value,
                request.Provider!.Value,
                new OpenAiSettingsValues(
                    openAi.BaseUrl!,
                    openAi.ApiMode!.Value,
                    openAi.Model!,
                    openAi.MaxTokens!.Value),
                new AnthropicSettingsValues(
                    anthropic.BaseUrl!,
                    anthropic.Model!,
                    anthropic.MaxTokens!.Value,
                    anthropic.ApiVersion!),
                new CodexAppServerSettingsValues(
                    codex.Endpoint!,
                    NullIfWhiteSpace(codex.Model),
                    codex.PermissionProfile!,
                    codex.TimeoutSeconds!.Value),
                new InferenceSettingsValues(inference.RateLimitDelayMs!.Value)),
            MapSecret(openAi.ApiKey, "ai.openAI.apiKey"),
            MapSecret(anthropic.ApiKey, "ai.anthropic.apiKey"),
            MapSecret(codex.Token, "ai.codexAppServer.token"));
    }

    private TorrentSettingsUpdate? MapTorrent(TorrentSettingsPatchRequest? request)
    {
        if (request is null)
            return null;
        if (request.Url is null)
        {
            AddRequired("torrent.url");
            return null;
        }

        return new TorrentSettingsUpdate(
            new TorrentSettingsValues(
                request.Url,
                NullIfWhiteSpace(request.UserName),
                NullIfWhiteSpace(request.UserAgent)),
            MapSecret(request.Password, "torrent.password"));
    }

    private MediaLibrarySettingsValues? MapMediaLibrary(MediaLibrarySettingsPatchRequest? request)
    {
        if (request is null)
            return null;
        if (request.AllowedRoots is null) AddRequired("mediaLibrary.allowedRoots");
        if (request.ScanInterval is null) AddRequired("mediaLibrary.scanInterval");
        if (request.SettlingPeriod is null) AddRequired("mediaLibrary.settlingPeriod");
        if (request.MissingGracePeriod is null) AddRequired("mediaLibrary.missingGracePeriod");
        return request.AllowedRoots is not null
               && request.ScanInterval is not null
               && request.SettlingPeriod is not null
               && request.MissingGracePeriod is not null
            ? new MediaLibrarySettingsValues(
                request.AllowedRoots,
                request.ScanInterval.Value,
                request.SettlingPeriod.Value,
                request.MissingGracePeriod.Value)
            : null;
    }

    private IncidentSettingsValues? MapIncidents(IncidentSettingsPatchRequest? request)
    {
        if (request is null)
            return null;
        if (request.DownloadStalledAfter is null) AddRequired("incidents.downloadStalledAfter");
        if (request.ReportThrottle is null) AddRequired("incidents.reportThrottle");
        if (request.ReconciliationInterval is null) AddRequired("incidents.reconciliationInterval");
        if (request.Disk is null)
        {
            AddRequired("incidents.disk");
            return null;
        }

        if (request.Disk.MinimumAvailableBytes is null)
            AddRequired("incidents.disk.minimumAvailableBytes");
        if (request.Disk.MinimumAvailablePercent is null)
            AddRequired("incidents.disk.minimumAvailablePercent");
        return request.DownloadStalledAfter is not null
               && request.ReportThrottle is not null
               && request.ReconciliationInterval is not null
               && request.Disk.MinimumAvailableBytes is not null
               && request.Disk.MinimumAvailablePercent is not null
            ? new IncidentSettingsValues(
                request.DownloadStalledAfter.Value,
                request.ReportThrottle.Value,
                request.ReconciliationInterval.Value,
                new IncidentDiskSettingsValues(
                    request.Disk.MinimumAvailableBytes.Value,
                    request.Disk.MinimumAvailablePercent.Value))
            : null;
    }

    private NfsSettingsValues? MapNfs(NfsSettingsPatchRequest? request)
    {
        if (request is null)
            return null;
        if (request.Enabled is null) AddRequired("nfs.enabled");
        if (request.Port is null) AddRequired("nfs.port");
        if (request.BindAddress is null) AddRequired("nfs.bindAddress");
        if (request.LeaseSeconds is null) AddRequired("nfs.leaseSeconds");
        if (request.MaxConnections is null) AddRequired("nfs.maxConnections");
        if (request.IdleTimeoutSeconds is null) AddRequired("nfs.idleTimeoutSeconds");
        if (request.AllowAnonymous is null) AddRequired("nfs.allowAnonymous");
        if (request.AllowedNetworks is null) AddRequired("nfs.allowedNetworks");
        return request.Enabled is not null
               && request.Port is not null
               && request.BindAddress is not null
               && request.LeaseSeconds is not null
               && request.MaxConnections is not null
               && request.IdleTimeoutSeconds is not null
               && request.AllowAnonymous is not null
               && request.AllowedNetworks is not null
            ? new NfsSettingsValues(
                request.Enabled.Value,
                request.Port.Value,
                request.BindAddress,
                request.LeaseSeconds.Value,
                request.MaxConnections.Value)
            {
                IdleTimeoutSeconds = request.IdleTimeoutSeconds.Value,
                AllowAnonymous = request.AllowAnonymous.Value,
                AllowedNetworks = request.AllowedNetworks
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray()
            }
            : null;
    }

    private NotificationSettingsUpdate? MapNotifications(
        NotificationSettingsPatchRequest? request)
    {
        if (request is null)
            return null;
        if (request.WebhookEnabled is null) AddRequired("notifications.webhookEnabled");
        if (request.WebPushEnabled is null) AddRequired("notifications.webPushEnabled");
        if (request.WebPushSubject is null) AddRequired("notifications.webPushSubject");
        if (request.Events is null) AddRequired("notifications.events");
        if (request.TimeZoneId is null) AddRequired("notifications.timeZoneId");
        if (!ModelState.IsValid)
            return null;

        return new NotificationSettingsUpdate(
            request.WebhookEnabled!.Value,
            request.WebPushEnabled!.Value,
            request.WebPushSubject!,
            request.Events!,
            request.QuietHoursStart,
            request.QuietHoursEnd,
            request.TimeZoneId!,
            MapSecret(request.WebhookUrl, "notifications.webhook.url"),
            request.GenerateVapidKeys);
    }

    private SecretMutation? MapSecret(SecretMutationRequest? request, string path)
    {
        if (request is null)
            return null;
        if (request.Operation is null)
        {
            AddRequired(path + ".operation");
            return null;
        }

        return new SecretMutation(request.Operation.Value, request.Value);
    }

    private void AddRequired(string key) =>
        ModelState.AddModelError(key, "The field is required.");

    private static ApplicationSettingsResponse ToResponse(RuntimeSettingsState state)
    {
        var values = state.Desired;
        return new ApplicationSettingsResponse(
            state.Revision,
            state.PendingRestart,
            new AiSettingsResponse(
                values.Ai.ExecutionMode,
                values.Ai.Provider,
                new OpenAiSettingsResponse(
                    values.Ai.OpenAI.BaseUrl,
                    values.Ai.OpenAI.ApiMode,
                    values.Ai.OpenAI.Model,
                    values.Ai.OpenAI.MaxTokens,
                    Secret(state, RuntimeSecretKeys.OpenAiApiKey)),
                new AnthropicSettingsResponse(
                    values.Ai.Anthropic.BaseUrl,
                    values.Ai.Anthropic.Model,
                    values.Ai.Anthropic.MaxTokens,
                    values.Ai.Anthropic.ApiVersion,
                    Secret(state, RuntimeSecretKeys.AnthropicApiKey)),
                new CodexAppServerSettingsResponse(
                    values.Ai.CodexAppServer.Endpoint,
                    values.Ai.CodexAppServer.Model ?? string.Empty,
                    values.Ai.CodexAppServer.PermissionProfile,
                    values.Ai.CodexAppServer.TimeoutSeconds,
                    Secret(state, RuntimeSecretKeys.CodexToken)),
                new InferenceSettingsResponse(values.Ai.Inference.RateLimitDelayMs)),
            new TmdbSettingsResponse(Secret(state, RuntimeSecretKeys.TmdbApiKey)),
            new TorrentSettingsResponse(
                values.Torrent.Url,
                values.Torrent.UserName ?? string.Empty,
                values.Torrent.UserAgent ?? string.Empty,
                Secret(state, RuntimeSecretKeys.TorrentPassword)),
            new MediaLibrarySettingsResponse(
                values.MediaLibrary.AllowedRoots,
                values.MediaLibrary.ScanInterval,
                values.MediaLibrary.SettlingPeriod,
                values.MediaLibrary.MissingGracePeriod),
            new IncidentSettingsResponse(
                values.Incidents.DownloadStalledAfter,
                values.Incidents.ReportThrottle,
                values.Incidents.ReconciliationInterval,
                new IncidentDiskSettingsResponse(
                    values.Incidents.Disk.MinimumAvailableBytes,
                    values.Incidents.Disk.MinimumAvailablePercent)),
            new NfsSettingsResponse(
                values.Nfs.Enabled,
                values.Nfs.Port,
                values.Nfs.BindAddress,
                values.Nfs.LeaseSeconds,
                values.Nfs.MaxConnections,
                values.Nfs.IdleTimeoutSeconds,
                values.Nfs.AllowAnonymous,
                values.Nfs.AllowedNetworks,
                RestartRequired: true,
                state.PendingRestart),
            new NotificationSettingsResponse(
                values.Notifications.WebhookEnabled,
                values.Notifications.WebPushEnabled,
                values.Notifications.WebPushSubject,
                values.Notifications.VapidPublicKey,
                Secret(state, RuntimeSecretKeys.NotificationVapidPrivateKey),
                values.Notifications.Events,
                values.Notifications.QuietHoursStart,
                values.Notifications.QuietHoursEnd,
                values.Notifications.TimeZoneId,
                Secret(state, RuntimeSecretKeys.NotificationWebhookUrl)));
    }

    private static SecretStateResponse Secret(RuntimeSettingsState state, string key)
    {
        var secret = state.Secrets[key];
        return new SecretStateResponse(secret.IsConfigured, secret.Source);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
