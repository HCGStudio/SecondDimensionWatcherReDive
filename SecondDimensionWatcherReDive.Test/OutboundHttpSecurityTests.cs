using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;
using SecondDimensionWatcherReDive.Utils.Http;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class OutboundHttpSecurityTests
{
    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("10.0.0.4")]
    [DataRow("169.254.169.254")]
    [DataRow("192.168.1.10")]
    [DataRow("::1")]
    [DataRow("fd00::1")]
    public void PrivateAndMetadataAddressesAreNotPublic(string value)
    {
        Assert.IsFalse(OutboundAddressPolicy.IsPubliclyRoutable(IPAddress.Parse(value)));
    }

    [TestMethod]
    public async Task PrivateLiteralIsRejected()
    {
        var policy = CreatePolicy(new StubResolver([]));

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            policy.ValidateUriAsync(new Uri("http://169.254.169.254/latest/meta-data"), CancellationToken.None));
    }

    [TestMethod]
    public async Task ExplicitCidrAllowlistPermitsPrivateFeed()
    {
        var options = new OutboundHttpOptions
        {
            AllowedPrivateNetworks = ["10.20.0.0/16"]
        };
        var policy = CreatePolicy(new StubResolver([IPAddress.Parse("10.20.3.4")]), options);

        await policy.ValidateUriAsync(new Uri("https://rss.home.example/feed"), CancellationToken.None);
    }

    [TestMethod]
    public async Task DnsRebindingIsCheckedAgainAtConnectionTime()
    {
        var resolver = new SequenceResolver(
            [IPAddress.Parse("93.184.216.34")],
            [IPAddress.Loopback]);
        var policy = CreatePolicy(resolver);

        await policy.ValidateUriAsync(new Uri("https://feed.example/rss"), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            policy.ResolveConnectionAddressAsync(
                new DnsEndPoint("feed.example", 443),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task RedirectToPrivateNetworkIsRejectedBeforeSecondRequest()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://127.0.0.1/secret") }
        });
        var fetcher = CreateFetcher(handler, new StubResolver([IPAddress.Parse("93.184.216.34")]));

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            fetcher.GetBytesAsync(
                "https://feed.example/rss",
                OutboundPayloadKind.Feed,
                CancellationToken.None));
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task ResponseBodyLimitIsEnforcedWithoutContentLength()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[17])
        });
        var options = new OutboundHttpOptions { MaxFeedBytes = 16 };
        var fetcher = CreateFetcher(
            handler,
            new StubResolver([IPAddress.Parse("93.184.216.34")]),
            options);

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            fetcher.GetBytesAsync(
                "https://feed.example/rss",
                OutboundPayloadKind.Feed,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task FirstByteTimeoutCancelsAStalledResponse()
    {
        var options = new OutboundHttpOptions
        {
            FirstByteTimeoutSeconds = 1,
            TotalTimeoutSeconds = 10
        };
        var fetcher = CreateFetcher(
            new StalledHandler(),
            new StubResolver([IPAddress.Parse("93.184.216.34")]),
            options);

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            fetcher.GetBytesAsync(
                "https://feed.example/rss",
                OutboundPayloadKind.Feed,
                CancellationToken.None));
    }

    private static OutboundAddressPolicy CreatePolicy(
        IHostAddressResolver resolver,
        OutboundHttpOptions? options = null) =>
        new(Options.Create(options ?? new OutboundHttpOptions()), resolver);

    private static SafeOutboundHttpFetcher CreateFetcher(
        HttpMessageHandler handler,
        IHostAddressResolver resolver,
        OutboundHttpOptions? options = null)
    {
        var configured = options ?? new OutboundHttpOptions();
        return new SafeOutboundHttpFetcher(
            new StubHttpClientFactory(new HttpClient(handler)),
            CreatePolicy(resolver, configured),
            Options.Create(configured));
    }

    private sealed class StubResolver(IPAddress[] addresses) : IHostAddressResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(
            string host,
            CancellationToken cancellationToken) => Task.FromResult(addresses);
    }

    private sealed class SequenceResolver(params IPAddress[][] results) : IHostAddressResolver
    {
        private int _index;

        public Task<IPAddress[]> GetHostAddressesAsync(
            string host,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, results.Length - 1);
            return Task.FromResult(results[index]);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class StalledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
