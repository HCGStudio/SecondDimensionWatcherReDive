using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace SecondDimensionWatcherReDive.Configuration;

internal sealed class TrustedProxyOptions
{
    internal const string SectionName = "ReverseProxy";

    [Range(1, 10)]
    public int ForwardLimit { get; set; } = 1;

    [Required]
    public string[] KnownProxies { get; set; } = [];

    [Required]
    public string[] KnownNetworks { get; set; } = [];
}

internal static class TrustedProxyConfiguration
{
    internal static bool IsValid(TrustedProxyOptions configured) =>
        configured.KnownProxies is not null &&
        configured.KnownNetworks is not null &&
        configured.KnownProxies.All(value => IPAddress.TryParse(value, out _)) &&
        configured.KnownNetworks.All(value => System.Net.IPNetwork.TryParse(value, out _));

    internal static void Apply(ForwardedHeadersOptions forwarded, TrustedProxyOptions configured)
    {
        forwarded.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.ForwardLimit = configured.ForwardLimit;
        forwarded.RequireHeaderSymmetry = true;

        foreach (var value in configured.KnownProxies)
        {
            if (!IPAddress.TryParse(value, out var proxy))
                throw new InvalidOperationException($"ReverseProxy:KnownProxies contains invalid address '{value}'.");
            if (!forwarded.KnownProxies.Contains(proxy))
                forwarded.KnownProxies.Add(proxy);
        }

        foreach (var value in configured.KnownNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(value, out var network))
                throw new InvalidOperationException($"ReverseProxy:KnownNetworks contains invalid CIDR '{value}'.");
            if (!forwarded.KnownIPNetworks.Contains(network))
                forwarded.KnownIPNetworks.Add(network);
        }
    }
}
