using System.Collections.Concurrent;
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
    Busy,
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
    private readonly SemaphoreSlim _fetchSlots;
    private readonly SemaphoreSlim _pendingSlots;
    private readonly ConcurrentDictionary<string, Lazy<Task<TmdbImageFetchResult>>> _inflight =
        new(StringComparer.Ordinal);
    private readonly CancellationToken _applicationStopping;

    public TmdbImageProxyService(
        IHttpClientFactory httpClientFactory,
        IOptions<TmdbImageProxyOptions> options,
        ILogger<TmdbImageProxyService> logger,
        IHostApplicationLifetime applicationLifetime)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _logger = logger;
        _options = options.Value;
        _fetchSlots = new SemaphoreSlim(_options.MaxConcurrentFetches, _options.MaxConcurrentFetches);
        _pendingSlots = new SemaphoreSlim(_options.MaxPendingFetches, _options.MaxPendingFetches);
        _applicationStopping = applicationLifetime.ApplicationStopping;
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

        Lazy<Task<TmdbImageFetchResult>>? candidate = null;
        candidate = new Lazy<Task<TmdbImageFetchResult>>(
            async () =>
            {
                try
                {
                    return await FetchAndCacheAsync(size, fileName, cacheKey, _applicationStopping);
                }
                finally
                {
                    if (_inflight.TryGetValue(cacheKey, out var current) &&
                        ReferenceEquals(current, candidate))
                    {
                        _inflight.TryRemove(cacheKey, out _);
                    }
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        var operation = _inflight.GetOrAdd(cacheKey, candidate);
        return await operation.Value.WaitAsync(cancellationToken);
    }

    private async Task<TmdbImageFetchResult> FetchAndCacheAsync(
        string size,
        string fileName,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var enteredFetchSlot = false;
        var enteredPendingSlot = false;

        try
        {
            enteredPendingSlot = await _pendingSlots.WaitAsync(0, cancellationToken);
            if (!enteredPendingSlot)
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Busy);

            await _fetchSlots.WaitAsync(cancellationToken);
            enteredFetchSlot = true;

            // A different request may have populated the cache while this fetch was
            // waiting for a global response-buffer slot.
            if (_cache.TryGetValue(cacheKey, out TmdbImageContent? cached) && cached is not null)
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Success, cached);

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
            if (bytes is null)
            {
                LogImageTooLarge(_logger, _options.MaxImageBytes + 1L, cacheKey);
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
            }
            if (bytes.Length == 0)
            {
                LogEmptyImage(_logger, cacheKey);
                return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
            }
            if (!HasExpectedSignature(bytes, contentType))
            {
                LogInvalidImageSignature(_logger, cacheKey, contentType);
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
        catch (IOException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestFailed(_logger, cacheKey, exception.GetType().Name);
            return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
        }
        catch (OperationCanceledException)
        {
            // Application shutdown cancels shared fetches. Individual request
            // cancellation is handled by WaitAsync in GetAsync and does not poison
            // other callers waiting for the same image.
            return new TmdbImageFetchResult(TmdbImageFetchStatus.Unavailable);
        }
        finally
        {
            if (enteredFetchSlot) _fetchSlots.Release();
            if (enteredPendingSlot) _pendingSlots.Release();
        }
    }

    private static bool IsValidPath(string size, string fileName) =>
        AllowedSizes.Contains(size) &&
        !fileName.Contains("..", StringComparison.Ordinal) &&
        TmdbFileNameRegex().IsMatch(fileName);

    private static bool HasExpectedSignature(ReadOnlySpan<byte> bytes, string contentType) =>
        contentType switch
        {
            "image/jpeg" => bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xd8,
            "image/png" => bytes.Length >= 8 &&
                           bytes[0] == 0x89 &&
                           bytes[1] == 0x50 &&
                           bytes[2] == 0x4e &&
                           bytes[3] == 0x47 &&
                           bytes[4] == 0x0d &&
                           bytes[5] == 0x0a &&
                           bytes[6] == 0x1a &&
                           bytes[7] == 0x0a,
            "image/webp" => bytes.Length >= 12 &&
                            bytes[..4].SequenceEqual("RIFF"u8) &&
                            bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            "image/avif" => bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8),
            _ => false
        };

    private async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        if (content.Headers.ContentLength is long declaredLength)
        {
            if (declaredLength > _options.MaxImageBytes) return null;
            if (declaredLength == 0) return [];

            var exact = GC.AllocateUninitializedArray<byte>(checked((int)declaredLength));
            var offset = 0;
            while (offset < exact.Length)
            {
                var read = await input.ReadAsync(exact.AsMemory(offset), cancellationToken);
                if (read == 0) return [];
                offset += read;
            }

            // Reject a body whose framing delivers more bytes than the declared and
            // already validated length.
            var trailingByte = new byte[1];
            return await input.ReadAsync(trailingByte, cancellationToken) == 0 ? exact : null;
        }

        using var output = new MemoryStream(Math.Min(_options.MaxImageBytes, 64 * 1024));
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

    public void Dispose()
    {
        _pendingSlots.Dispose();
        _fetchSlots.Dispose();
        _cache.Dispose();
    }

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
        Message = "TMDB image {ImagePath} returned an empty or truncated response body")]
    private static partial void LogEmptyImage(ILogger logger, string imagePath);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "TMDB image {ImagePath} did not match its declared image type {ContentType}")]
    private static partial void LogInvalidImageSignature(ILogger logger, string imagePath, string contentType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "TMDB image request failed for {ImagePath}: {Reason}")]
    private static partial void LogRequestFailed(ILogger logger, string imagePath, string reason);
}
