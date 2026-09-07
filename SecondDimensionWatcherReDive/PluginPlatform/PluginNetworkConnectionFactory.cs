using System.Net;
using System.Net.Sockets;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal interface IPluginDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemPluginDnsResolver : IPluginDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        => IPAddress.TryParse(host, out var address)
            ? Task.FromResult(new[] { address })
            : Dns.GetHostAddressesAsync(host, cancellationToken);
}

internal static class PluginNetworkConnectionFactory
{
    public static SocketsHttpHandler Create(IPluginDnsResolver resolver)
        => new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
                ConnectAsync(context.DnsEndPoint, resolver, cancellationToken)
        };

    private static async ValueTask<Stream> ConnectAsync(
        DnsEndPoint endpoint,
        IPluginDnsResolver resolver,
        CancellationToken cancellationToken)
    {
        var addresses = await resolver.ResolveAsync(endpoint.Host, cancellationToken);
        if (addresses.Length == 0)
            throw new HttpRequestException($"Plugin network target '{endpoint.Host}' did not resolve.");
        if (addresses.Any(address => !IsPublicAddress(address)))
            throw new UnauthorizedAccessException(
                $"Plugin network target '{endpoint.Host}' resolved to a non-public address.");

        List<Exception>? failures = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                socket.Dispose();
                (failures ??= []).Add(exception);
            }
        }

        throw new HttpRequestException(
            $"Could not connect to approved plugin network target '{endpoint.Host}'.",
            failures is { Count: 1 } ? failures[0] : new AggregateException(failures ?? []));
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !InCidr(bytes, [0, 0, 0, 0], 8) &&
                   !InCidr(bytes, [10, 0, 0, 0], 8) &&
                   !InCidr(bytes, [100, 64, 0, 0], 10) &&
                   !InCidr(bytes, [127, 0, 0, 0], 8) &&
                   !InCidr(bytes, [169, 254, 0, 0], 16) &&
                   !InCidr(bytes, [172, 16, 0, 0], 12) &&
                   !InCidr(bytes, [192, 0, 0, 0], 24) &&
                   !InCidr(bytes, [192, 0, 2, 0], 24) &&
                   !InCidr(bytes, [192, 88, 99, 0], 24) &&
                   !InCidr(bytes, [192, 168, 0, 0], 16) &&
                   !InCidr(bytes, [198, 18, 0, 0], 15) &&
                   !InCidr(bytes, [198, 51, 100, 0], 24) &&
                   !InCidr(bytes, [203, 0, 113, 0], 24) &&
                   !InCidr(bytes, [224, 0, 0, 0], 4) &&
                   !InCidr(bytes, [240, 0, 0, 0], 4);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        return InCidr(bytes, [0x20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 3) &&
               !address.Equals(IPAddress.IPv6Any) &&
               !address.Equals(IPAddress.IPv6Loopback) &&
               !address.IsIPv6LinkLocal &&
               !address.IsIPv6Multicast &&
               !InCidr(bytes, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 96) &&
               !InCidr(bytes, [0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0, 0, 0, 0, 0, 0], 96) &&
               !InCidr(bytes, [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 96) &&
               !InCidr(bytes, [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 48) &&
               !InCidr(bytes, [0xfc, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 7) &&
               !InCidr(bytes, [0xfe, 0xc0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 10) &&
               !InCidr(bytes, [0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 64) &&
               !InCidr(bytes, [0x20, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 23) &&
               !InCidr(bytes, [0x20, 0x01, 0x00, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 48) &&
               !InCidr(bytes, [0x20, 0x01, 0x0d, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 32) &&
               !InCidr(bytes, [0x20, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 16) &&
               !InCidr(bytes, [0x3f, 0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 20);
    }

    private static bool InCidr(ReadOnlySpan<byte> address, ReadOnlySpan<byte> network, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        if (!address[..wholeBytes].SequenceEqual(network[..wholeBytes])) return false;
        if (remainingBits == 0) return true;
        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (network[wholeBytes] & mask);
    }
}
