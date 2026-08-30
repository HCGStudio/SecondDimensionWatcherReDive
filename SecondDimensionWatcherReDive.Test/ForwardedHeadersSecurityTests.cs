using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ForwardedHeadersSecurityTests
{
    [TestMethod]
    public async Task LoopbackProxyRewritesClientAddressBeforeDownstreamMiddleware()
    {
        var forwardedOptions = new ForwardedHeadersOptions();
        TrustedProxyConfiguration.Apply(forwardedOptions, new TrustedProxyOptions());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.25";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        IPAddress? observedAddress = null;
        string? observedScheme = null;
        var middleware = new ForwardedHeadersMiddleware(
            next: nextContext =>
            {
                observedAddress = nextContext.Connection.RemoteIpAddress;
                observedScheme = nextContext.Request.Scheme;
                return Task.CompletedTask;
            },
            NullLoggerFactory.Instance,
            Options.Create(forwardedOptions));

        await middleware.Invoke(context);

        Assert.AreEqual(IPAddress.Parse("203.0.113.25"), observedAddress);
        Assert.AreEqual("https", observedScheme);
    }

    [TestMethod]
    public async Task UntrustedPeerCannotSpoofForwardedClientAddress()
    {
        var forwardedOptions = new ForwardedHeadersOptions();
        TrustedProxyConfiguration.Apply(forwardedOptions, new TrustedProxyOptions());
        var peer = IPAddress.Parse("198.51.100.44");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = peer;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.25";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        IPAddress? observedAddress = null;
        var middleware = new ForwardedHeadersMiddleware(
            next: nextContext =>
            {
                observedAddress = nextContext.Connection.RemoteIpAddress;
                return Task.CompletedTask;
            },
            NullLoggerFactory.Instance,
            Options.Create(forwardedOptions));

        await middleware.Invoke(context);

        Assert.AreEqual(peer, observedAddress);
    }

    [TestMethod]
    public void AdditionalTrustedProxyNetworkRequiresValidCidr()
    {
        var forwardedOptions = new ForwardedHeadersOptions();
        TrustedProxyConfiguration.Apply(forwardedOptions, new TrustedProxyOptions
        {
            KnownNetworks = ["10.20.0.0/16"]
        });

        Assert.IsTrue(forwardedOptions.KnownIPNetworks.Any(network =>
            network.Contains(IPAddress.Parse("10.20.1.2"))));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            TrustedProxyConfiguration.Apply(new ForwardedHeadersOptions(), new TrustedProxyOptions
            {
                KnownNetworks = ["not-a-cidr"]
            }));
    }
}
