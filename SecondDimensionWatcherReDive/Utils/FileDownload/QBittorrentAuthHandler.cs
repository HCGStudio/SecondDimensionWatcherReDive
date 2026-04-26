using System.Net;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public sealed partial class QBittorrentAuthHandler(
    IConfiguration configuration,
    ILogger<QBittorrentAuthHandler> logger)
    : DelegatingHandler
{
    private const string LoginPath = "/api/v2/auth/login";

    private readonly string? _userName = configuration["Torrent:Remote:UserName"];
    private readonly string? _password = configuration["Torrent:Remote:Password"];
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private bool _hasLoggedIn;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userName))
            return await base.SendAsync(request, cancellationToken);

        if (request.RequestUri?.AbsolutePath == LoginPath)
            return await base.SendAsync(request, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return response;

        response.Dispose();
        var loggedIn = await EnsureLoggedInAsync(forceRefresh: true, cancellationToken);
        if (!loggedIn)
            return await base.SendAsync(request, cancellationToken);

        return await base.SendAsync(await CloneRequestAsync(request, cancellationToken), cancellationToken);
    }

    private async Task<bool> EnsureLoggedInAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (_hasLoggedIn && !forceRefresh)
                return true;

            using var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("username", _userName!),
                new KeyValuePair<string, string>("password", _password ?? string.Empty)
            ]);
            using var loginRequest = new HttpRequestMessage(HttpMethod.Post, LoginPath) { Content = content };
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
                LogLoginRejected(logger, body.Trim());
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
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };

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

        return clone;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent login HTTP {StatusCode}.")]
    private static partial void LogLoginHttpFailed(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent login rejected: {Body}")]
    private static partial void LogLoginRejected(ILogger logger, string body);
}
