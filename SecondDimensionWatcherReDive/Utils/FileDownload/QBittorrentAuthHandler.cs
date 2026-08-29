using System.Net;
using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public sealed partial class QBittorrentAuthHandler(
    IOptionsMonitor<QBittorrentRemoteOptions> options,
    QBittorrentCookieStore cookieStore,
    ILogger<QBittorrentAuthHandler> logger)
    : DelegatingHandler
{
    private const string LoginPath = "/api/v2/auth/login";
    private const string LogoutPath = "/api/v2/auth/logout";

    private sealed record SessionSettings(
        Uri Endpoint,
        string? UserName,
        string? Password);

    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private SessionSettings? _sessionSettings;
    private bool _hasLoggedIn;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // HttpClientHandler applies response cookies before SendAsync completes. Serializing the
        // full exchange makes cookie selection and endpoint rotation atomic, preventing an old
        // same-host request from receiving or sending a new port's SID.
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            return await SendSerializedAsync(request, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendSerializedAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == LoginPath)
            return await base.SendAsync(request, cancellationToken);

        var settings = GetSessionSettings(request);
        var credentialsChanged = await RefreshCredentialsAsync(settings, cancellationToken);

        if (string.IsNullOrEmpty(settings.UserName))
            return await base.SendAsync(request, cancellationToken);

        // Refresh the qBittorrent cookie before sending a request with newly saved
        // credentials. Otherwise the pooled primary handler could keep using the old session.
        if (credentialsChanged)
            await EnsureLoggedInAsync(forceRefresh: false, settings, cancellationToken);

        // Buffer a retry before the first send. Copying content after a 403 loses
        // non-seekable request bodies because the primary handler has already consumed them.
        using var retryRequest = await CloneRequestAsync(request, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return response;

        var loggedIn = await EnsureLoggedInAsync(
            forceRefresh: true,
            settings,
            cancellationToken);
        if (!loggedIn)
            return response;

        response.Dispose();
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private SessionSettings GetSessionSettings(HttpRequestMessage request)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } requestUri)
            throw new InvalidOperationException("qBittorrent requests must have an absolute URI.");

        var settings = GetCurrentSessionSettings();
        var requestEndpoint = new Uri(requestUri.GetLeftPart(UriPartial.Authority) + "/");
        if (requestEndpoint != settings.Endpoint)
            throw new HttpRequestException(
                "The qBittorrent endpoint changed after this client was created; retry with a new client.");

        return settings;
    }

    private SessionSettings GetCurrentSessionSettings()
    {
        var current = options.CurrentValue;
        if (!Uri.TryCreate(current.Url, UriKind.Absolute, out var configuredUri)
            || configuredUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(configuredUri.UserInfo)
            || !string.IsNullOrEmpty(configuredUri.Query)
            || !string.IsNullOrEmpty(configuredUri.Fragment))
            throw new InvalidOperationException("The configured qBittorrent URL is invalid.");

        var configuredEndpoint = new Uri(configuredUri.GetLeftPart(UriPartial.Authority) + "/");
        return new(
            configuredEndpoint,
            current.UserName,
            current.Password);
    }

    private async Task<bool> RefreshCredentialsAsync(
        SessionSettings settings,
        CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            // This request may have captured its snapshot before a newer request rotated the
            // shared cookie session. Never let the waiter restore stale credentials after it
            // acquires the lock.
            if (GetCurrentSessionSettings() != settings)
                throw new HttpRequestException(
                    "The qBittorrent credentials changed while this request was waiting; retry with current settings.");

            if (_sessionSettings == settings) return false;

            // An endpoint rotation must not depend on the retired server accepting a logout.
            // Clear the dedicated local jar instead: cookies do not distinguish ports, and a
            // retained SID could otherwise cross ports or be reused after an A -> B -> A change.
            // On the same endpoint, logout remains mandatory so the server-side session is
            // invalidated before changed or cleared credentials take effect.
            var endpointChanged = _sessionSettings is not null
                                  && _sessionSettings.Endpoint != settings.Endpoint;
            if (_hasLoggedIn && _sessionSettings is not null && !endpointChanged)
            {
                try
                {
                    using var logoutRequest = new HttpRequestMessage(
                        HttpMethod.Post,
                        new Uri(_sessionSettings.Endpoint, LogoutPath));
                    using var logoutResponse = await base.SendAsync(logoutRequest, cancellationToken);
                    if (!logoutResponse.IsSuccessStatusCode)
                    {
                        LogLogoutHttpFailed(logger, (int)logoutResponse.StatusCode);
                        throw new HttpRequestException(
                            $"qBittorrent logout returned HTTP {(int)logoutResponse.StatusCode}.");
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogLogoutFailed(logger, exception);
                    throw new HttpRequestException(
                        "Unable to invalidate the previous qBittorrent session; the request was blocked.",
                        exception);
                }
            }

            // The jar is dedicated to this handler pipeline. Clear it after a successful
            // same-endpoint logout, or immediately when changing endpoints. Also clear cookies
            // left by a rejected login before attempting a different credential set.
            if (_sessionSettings is not null)
                cookieStore.Clear();

            _sessionSettings = settings;
            _hasLoggedIn = false;
            return true;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task<bool> EnsureLoggedInAsync(
        bool forceRefresh,
        SessionSettings settings,
        CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            // Another request may have observed a newer endpoint or credential set while
            // this request was in flight. Never restore the stale session in that case.
            if (_sessionSettings != settings || string.IsNullOrEmpty(settings.UserName))
                return false;

            if (_hasLoggedIn && !forceRefresh)
                return true;

            using var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", settings.UserName),
                new KeyValuePair<string, string>("password", settings.Password ?? string.Empty)
            ]);
            using var loginRequest = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(settings.Endpoint, LoginPath))
            {
                Content = content
            };
            using var loginResponse = await base.SendAsync(loginRequest, cancellationToken);
            if (!loginResponse.IsSuccessStatusCode)
            {
                LogLoginHttpFailed(logger, (int)loginResponse.StatusCode);
                _hasLoggedIn = false;
                return false;
            }

            var body = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!string.Equals(body.Trim(), "Ok.", StringComparison.Ordinal))
            {
                LogLoginRejected(logger);
                _hasLoggedIn = false;
                return false;
            }

            _hasLoggedIn = true;
            return true;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage original,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy
        };

        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync(cancellationToken);
            var copy = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = copy;
        }

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in original.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        return clone;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent login HTTP {StatusCode}.")]
    private static partial void LogLoginHttpFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent login rejected")]
    private static partial void LogLoginRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent logout failed while rotating credentials")]
    private static partial void LogLogoutFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent logout HTTP {StatusCode} while rotating credentials")]
    private static partial void LogLogoutHttpFailed(ILogger logger, int statusCode);
}
