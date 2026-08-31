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
    [DataRow("::192.168.1.10")]
    [DataRow("64:ff9b::7f00:1")]
    [DataRow("64:ff9b:1::a00:1")]
    [DataRow("2002:7f00:1::")]
    [DataRow("2001:0000:4136:e378:8000:63bf:3fff:fdd2")]
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
    [DataRow("::192.168.1.10")]
    [DataRow("64:ff9b::7f00:1")]
    [DataRow("64:ff9b:1::a00:1")]
    [DataRow("2002:7f00:1::")]
    [DataRow("2001:0000:4136:e378:8000:63bf:3fff:fdd2")]
    public async Task EmbeddedIpv4LiteralIsRejected(string value)
    {
        var policy = CreatePolicy(new StubResolver([]));

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            policy.ValidateUriAsync(new Uri($"http://[{value}]/feed"), CancellationToken.None));
    }

    [TestMethod]
    public async Task EmbeddedIpv4DnsAnswerIsRejected()
    {
        var policy = CreatePolicy(new StubResolver([IPAddress.Parse("64:ff9b::7f00:1")]));

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            policy.ValidateUriAsync(new Uri("https://feed.example/rss"), CancellationToken.None));
    }

    [TestMethod]
    public async Task ConnectCallbackRejectsEmbeddedIpv4BeforeOpeningSocket()
    {
        var policy = CreatePolicy(new StubResolver([IPAddress.Parse("2002:7f00:1::")]));
        var connector = new StubSocketConnector((_, _) =>
            Task.FromException<Stream>(new AssertFailedException("Connector must not be called.")));
        var factory = new OutboundConnectionFactory(
            policy,
            connector,
            Options.Create(new OutboundHttpOptions()));

        await Assert.ThrowsExactlyAsync<OutboundRequestBlockedException>(() =>
            factory.ConnectAsync(
                new DnsEndPoint("feed.example", 443),
                CancellationToken.None).AsTask());
        Assert.IsEmpty(connector.Attempts);
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
            policy.ResolveConnectionAddressesAsync(
                new DnsEndPoint("feed.example", 443),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task DualStackConnectionFallsBackWhenIpv6IsUnreachable()
    {
        var ipv6 = IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946");
        var ipv4 = IPAddress.Parse("93.184.216.34");
        var policy = CreatePolicy(new StubResolver([ipv6, ipv4]));
        var connector = new StubSocketConnector((address, _) =>
            address.AddressFamily == AddressFamily.InterNetworkV6
                ? Task.FromException<Stream>(new SocketException((int)SocketError.NetworkUnreachable))
                : Task.FromResult<Stream>(new MemoryStream()));
        var options = Options.Create(new OutboundHttpOptions
        {
            HappyEyeballsDelayMilliseconds = 0
        });
        var factory = new OutboundConnectionFactory(policy, connector, options);

        await using var stream = await factory.ConnectAsync(
            new DnsEndPoint("dual-stack.example", 443),
            CancellationToken.None);

        CollectionAssert.Contains(connector.Attempts, ipv6);
        CollectionAssert.Contains(connector.Attempts, ipv4);
    }

    [TestMethod]
    public async Task ConnectionCandidatesInterleaveIpv6AndIpv4()
    {
        var firstIpv6 = IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946");
        var secondIpv6 = IPAddress.Parse("2606:4700:4700::1111");
        var ipv4 = IPAddress.Parse("93.184.216.34");
        var policy = CreatePolicy(new StubResolver([firstIpv6, secondIpv6, ipv4]));

        var candidates = await policy.ResolveConnectionAddressesAsync(
            new DnsEndPoint("dual-stack.example", 443),
            CancellationToken.None);

        CollectionAssert.AreEqual(new[] { firstIpv6, ipv4, secondIpv6 }, candidates);
    }

    [TestMethod]
    public async Task HappyEyeballsDoesNotWaitForStalledFirstAddress()
    {
        var ipv6 = IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946");
        var ipv4 = IPAddress.Parse("93.184.216.34");
        var policy = CreatePolicy(new StubResolver([ipv6, ipv4]));
        var connector = new StubSocketConnector(async (address, cancellationToken) =>
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new AssertFailedException("The stalled IPv6 attempt should be cancelled.");
            }
            return new MemoryStream();
        });
        var options = Options.Create(new OutboundHttpOptions
        {
            HappyEyeballsDelayMilliseconds = 1
        });
        var factory = new OutboundConnectionFactory(policy, connector, options);

        await using var stream = await factory.ConnectAsync(
            new DnsEndPoint("dual-stack.example", 443),
            CancellationToken.None);

        CollectionAssert.Contains(connector.Attempts, ipv4);
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

    private sealed class StubSocketConnector(
        Func<IPAddress, CancellationToken, Task<Stream>> connect) : IOutboundSocketConnector
    {
        private readonly Lock _gate = new();

        public List<IPAddress> Attempts { get; } = [];

        public Task<Stream> ConnectAsync(
            IPAddress address,
            int port,
            CancellationToken cancellationToken)
        {
            lock (_gate)
                Attempts.Add(address);
            return connect(address, cancellationToken);
        }
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
