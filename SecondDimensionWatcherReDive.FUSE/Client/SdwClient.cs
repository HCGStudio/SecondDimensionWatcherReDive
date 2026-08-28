using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SecondDimensionWatcherReDive.FUSE.Client;

internal sealed class SdwUnauthorizedException(string message) : Exception(message);

internal sealed class SdwClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<SdwClient> _logger;
    private bool _disposed;

    public SdwClient(Uri serverBaseUrl, string username, string password, string userAgent, ILogger<SdwClient> logger)
        : this(serverBaseUrl, username, password, userAgent, logger, new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.None,
        })
    {
    }

    internal SdwClient(Uri serverBaseUrl, string username, string password, string userAgent,
        ILogger<SdwClient> logger, HttpMessageHandler handler)
    {
        _logger = logger;
        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = serverBaseUrl,
            Timeout = TimeSpan.FromSeconds(60),
        };
        var bytes = Encoding.UTF8.GetBytes($"{username}:{password}");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(bytes));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<VfsEntry?> StatAsync(string virtualPath, CancellationToken cancellationToken)
    {
        var url = $"/api/vfs/stat?path={EncodePath(virtualPath)}";
        using var response = await SendWithRetryAsync(HttpMethod.Get, url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccess(response, url);
        return await response.Content.ReadFromJsonAsync(SdwJsonContext.Default.VfsEntry, cancellationToken);
    }

    public async Task<VfsEntry[]?> ListAsync(string virtualPath, CancellationToken cancellationToken)
    {
        var url = $"/api/vfs/list?path={EncodePath(virtualPath)}";
        using var response = await SendWithRetryAsync(HttpMethod.Get, url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.BadRequest) return null;
        EnsureSuccess(response, url);
        return await response.Content.ReadFromJsonAsync(SdwJsonContext.Default.VfsEntryArray, cancellationToken);
    }

    public async Task<int> ReadAsync(string virtualPath, long offset, byte[] buffer, int bufferOffset, int count,
        CancellationToken cancellationToken)
    {
        var url = $"/api/vfs/read?path={EncodePath(virtualPath)}";
        using var response = await SendWithRetryAsync(HttpMethod.Get, url, cancellationToken,
            request => request.Headers.Range = new RangeHeaderValue(offset, offset + count - 1),
            HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound) return -Native.Errno.ENOENT;
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return 0;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SdwUnauthorizedException("Server rejected credentials.");
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("VFS read failed: {Status} {Url}", (int)response.StatusCode, url);
            return -Native.Errno.EIO;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var span = buffer.AsMemory(bufferOffset, count);
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(span[total..], cancellationToken);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string url,
        CancellationToken cancellationToken, Action<HttpRequestMessage>? configureRequest = null,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        const int MaxAttempts = 3;
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);
                configureRequest?.Invoke(request);
                var response = await _http.SendAsync(request, completionOption, cancellationToken);
                if ((int)response.StatusCode >= 500 && attempt < MaxAttempts)
                {
                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                last = ex;
                _logger.LogWarning(ex, "Transient HTTP failure on attempt {Attempt} for {Url}", attempt, url);
                await DelayBeforeRetryAsync(attempt, cancellationToken);
            }
        }
        throw new IOException($"Request to {url} failed after {MaxAttempts} attempts.", last);
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt - 1)), cancellationToken);

    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SdwUnauthorizedException($"Server rejected credentials for {url}.");
        if (!response.IsSuccessStatusCode)
            throw new IOException($"VFS request {url} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    // ASP.NET model binding URL-decodes the `path` query parameter for us, so we just
    // need to make sure each path segment survives transport — encode `?`, `#`, `&`,
    // spaces, Unicode, etc. We keep `/` literal so the server sees a real path string.
    internal static string EncodePath(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/") return "/";
        var segments = virtualPath.Split('/', StringSplitOptions.None);
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length == 0) continue;
            segments[i] = Uri.EscapeDataString(segments[i]);
        }
        return string.Join('/', segments);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}
