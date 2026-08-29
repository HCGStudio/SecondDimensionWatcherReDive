using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.Services;

internal enum TmdbImageFetchStatus
{
    Success,
    InvalidPath,
    NotFound,
    Unavailable
}

internal sealed record TmdbImageContent(
    byte[] Bytes,
    string ContentType,
    string ETag);

internal sealed record TmdbImageFetchResult(
    TmdbImageFetchStatus Status,
    TmdbImageContent? Content = null);

internal interface ITmdbImageProxyService
{
    Task<TmdbImageFetchResult> GetAsync(
        string size,
        string fileName,
        CancellationToken cancellationToken);
}

internal sealed partial class TmdbImageProxyService : ITmdbImageProxyService, IDisposable
{
    private const string HttpClientName = "TmdbImages";

    private static readonly HashSet<string> AllowedSizes =
    [
        "w92",
        "w154",
        "w185",
        "w300",
        "w342",
        "w500",
        "w780",
        "original"
    ];

    private static readonly HashSet<string> AllowedContentTypes =
    [
        "image/avif",
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    private readonly HttpClient _httpClient;
    private readonly ILogger<TmdbImageProxyService> _logger;
    private readonly TmdbImageProxyOptions _options;
    private readonly MemoryCache _cache;

    public TmdbImageProxyService(
        IHttpClientFactory httpClientFactory,
        IOptions<TmdbImageProxyOptions> options,
        ILogger<TmdbImageProxyService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _logger = logger;
        _options = options.Value;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.CacheSizeBytes
        });
    }

    public async Task<TmdbImageFetchResult> GetAsync(
        string size,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (!IsValidPath(size, fileName))
            return new TmdbImageFetchResult(TmdbImageFetchStatus.InvalidPath);

        var cacheKey = $"{size}/{fileName}";
        if (_cache.TryGetValue(cacheKey, out TmdbImageContent? cached) && cached is not null)
            return new TmdbImageFetchResult(TmdbImageFetchStatus.Success, cached);

        try
        {
            var requestPath = $"{size}/{Uri.EscapeDataString(fileName)}";
            using var response = await _httpClient.GetAsync(
                requestPath,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new TmdbImageFetchResult(TmdbImageFetchStatus.NotFound);

            if (!response.IsSuccessStatusCode)
            {
                LogUpstreamFailure(_logger, (int)response.StatusCode, cacheKey);
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (contentType is null || !AllowedContentTypes.Contains(contentType))
            {
                LogUnexpectedContentType(_logger, contentType ?? "(missing)", cacheKey);
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > _options.MaxImageBytes)
            {
                LogImageTooLarge(_logger, contentLength.Value, cacheKey);
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
            }

            var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                LogImageTooLarge(_logger, _options.MaxImageBytes + 1L, cacheKey);
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var content = new TmdbImageContent(bytes, contentType, $"\"{hash}\"");

            // Images larger than the complete cache budget are still returned, but never
            // inserted. MemoryCache uses byte length as its entry size and enforces the
            // configured aggregate SizeLimit for all other entries.
            if (bytes.LongLength <= _options.CacheSizeBytes)
            {
                _cache.Set(
                    cacheKey,
                    content,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _options.CacheDuration,
                        Size = bytes.LongLength
                    });
            }

            return new TmdbImageFetchResult(TmdbImageFetchStatus.Success, content);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(_logger, cacheKey, "timeout");
            return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
        }
        catch (HttpRequestException exception)
        {
            LogRequestFailed(_logger, cacheKey, exception.GetType().Name);
            return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
        }
    }

    private static bool IsValidPath(string size, string fileName) =>
        AllowedSizes.Contains(size) &&
        !fileName.Contains("..", StringComparison.Ordinal) &&
        TmdbFileNameRegex().IsMatch(fileName);

    private async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > _options.MaxImageBytes) return null;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    public void Dispose() => _cache.Dispose();

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,199}\\.(?:avif|jpe?g|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TmdbFileNameRegex();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "TMDB image upstream returned HTTP {StatusCode} for {ImagePath}")]
    private static partial void LogUpstreamFailure(ILogger logger, int statusCode, string imagePath);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "TMDB image upstream returned content type {ContentType} for {ImagePath}")]
    private static partial void LogUnexpectedContentType(ILogger logger, string contentType, string imagePath);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "TMDB image {ImagePath} exceeded the configured limit; observed at least {ObservedBytes} bytes")]
    private static partial void LogImageTooLarge(ILogger logger, long observedBytes, string imagePath);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "TMDB image request failed for {ImagePath}: {Reason}")]
    private static partial void LogRequestFailed(ILogger logger, string imagePath, string reason);
}
