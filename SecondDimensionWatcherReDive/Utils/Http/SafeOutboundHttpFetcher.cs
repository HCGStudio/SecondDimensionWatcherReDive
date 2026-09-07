using System.Buffers;
using System.Net;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Utils.Http;

internal enum OutboundPayloadKind
{
    Feed,
    Torrent
}

internal interface ISafeOutboundHttpFetcher
{
    Task<byte[]> GetBytesAsync(
        string url,
        OutboundPayloadKind payloadKind,
        CancellationToken cancellationToken);

    Task ValidateUrlAsync(string url, CancellationToken cancellationToken);
}

internal sealed class SafeOutboundHttpFetcher : ISafeOutboundHttpFetcher, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OutboundAddressPolicy _addressPolicy;
    private readonly OutboundHttpOptions _options;
    private readonly SemaphoreSlim _concurrency;

    public SafeOutboundHttpFetcher(
        IHttpClientFactory httpClientFactory,
        OutboundAddressPolicy addressPolicy,
        IOptions<OutboundHttpOptions> options)
    {
        _httpClient = httpClientFactory.CreateClient("SafeFeed");
        _addressPolicy = addressPolicy;
        _options = options.Value;
        _concurrency = new SemaphoreSlim(_options.MaxConcurrentRequests);
    }

    public async Task ValidateUrlAsync(string url, CancellationToken cancellationToken)
    {
        var uri = ParseUri(url);
        await _addressPolicy.ValidateUriAsync(uri, cancellationToken);
    }

    public async Task<byte[]> GetBytesAsync(
        string url,
        OutboundPayloadKind payloadKind,
        CancellationToken cancellationToken)
    {
        var maximumBytes = payloadKind switch
        {
            OutboundPayloadKind.Feed => _options.MaxFeedBytes,
            OutboundPayloadKind.Torrent => _options.MaxTorrentBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(payloadKind))
        };

        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TotalTimeoutSeconds));
            try
            {
                return await GetWithRedirectsAsync(ParseUri(url), maximumBytes, timeout.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new OutboundRequestBlockedException("The outbound request exceeded its total deadline.");
            }
        }
        finally
        {
            _concurrency.Release();
        }
    }

    public void Dispose()
    {
        _concurrency.Dispose();
    }

    private async Task<byte[]> GetWithRedirectsAsync(
        Uri initialUri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var redirect = 0; ; redirect++)
        {
            await _addressPolicy.ValidateUriAsync(current, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var firstByteTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            firstByteTimeout.CancelAfter(TimeSpan.FromSeconds(_options.FirstByteTimeoutSeconds));
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    firstByteTimeout.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && firstByteTimeout.IsCancellationRequested)
            {
                throw new OutboundRequestBlockedException(
                    "The outbound server did not send response headers before the deadline.");
            }
            using (response)
            {
                if (IsRedirect(response.StatusCode))
                {
                    if (redirect >= _options.MaxRedirects || response.Headers.Location is null)
                        throw new OutboundRequestBlockedException("The outbound redirect limit was exceeded.");
                    current = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(current, response.Headers.Location);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } contentLength &&
                    contentLength > maximumBytes)
                    throw new OutboundRequestBlockedException("The outbound response is larger than allowed.");

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await ReadBoundedAsync(stream, maximumBytes, cancellationToken);
            }
        }
    }

    private static Uri ParseUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new OutboundRequestBlockedException("The outbound URL is invalid.");
        OutboundAddressPolicy.ValidateUriShape(uri);
        return uri;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.Moved or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    return output.ToArray();
                if (output.Length + read > maximumBytes)
                    throw new OutboundRequestBlockedException("The outbound response is larger than allowed.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
