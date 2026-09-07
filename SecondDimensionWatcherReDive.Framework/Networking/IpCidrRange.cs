using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SecondDimensionWatcherReDive.Framework.Networking;

public readonly struct IpCidrRange
{
    private readonly byte[]? _network;

    private IpCidrRange(byte[] network, int prefixLength)
    {
        _network = network;
        PrefixLength = prefixLength;
    }

    public int PrefixLength { get; }

    public static bool TryParse(string? value, bool requirePrefix, out IpCidrRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOf('/');
        if (requirePrefix && separator < 0)
            return false;

        var addressText = separator < 0 ? trimmed : trimmed[..separator];
        if (!IPAddress.TryParse(addressText, out var address))
            return false;

        var originalMaximumPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = separator < 0
            ? originalMaximumPrefix
            : int.TryParse(
                trimmed[(separator + 1)..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : -1;
        if (prefix < 0 || prefix > originalMaximumPrefix)
            return false;

        if (address.IsIPv4MappedToIPv6)
        {
            // An IPv4-mapped IPv6 CIDR only describes an IPv4 network when the prefix
            // includes the fixed 96-bit ::ffff:0:0/96 mapping prefix.
            if (prefix < 96)
                return false;
            address = address.MapToIPv4();
            prefix -= 96;
        }

        var network = address.GetAddressBytes();
        ApplyMask(network, prefix);
        range = new IpCidrRange(network, prefix);
        return true;
    }

    public bool Contains(IPAddress input)
    {
        if (_network is null)
            return false;

        var address = input.IsIPv4MappedToIPv6 ? input.MapToIPv4() : input;
        var bytes = address.GetAddressBytes();
        if (bytes.Length != _network.Length)
            return false;

        var fullBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;
        if (!bytes.AsSpan(0, fullBytes).SequenceEqual(_network.AsSpan(0, fullBytes)))
            return false;
        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xff << (8 - remainingBits));
        return (bytes[fullBytes] & mask) == (_network[fullBytes] & mask);
    }

    public override string ToString() =>
        _network is null ? string.Empty : $"{new IPAddress(_network)}/{PrefixLength}";

    private static void ApplyMask(Span<byte> bytes, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        if (fullBytes >= bytes.Length)
            return;

        if (remainingBits > 0)
        {
            bytes[fullBytes] &= (byte)(0xff << (8 - remainingBits));
            fullBytes++;
        }
        bytes[fullBytes..].Clear();
    }
}
