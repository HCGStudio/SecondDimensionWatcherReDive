using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Configuration;

namespace SecondDimensionWatcherReDive.Utils.Http;

internal interface IHostAddressResolver
{
    Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemHostAddressResolver : IHostAddressResolver
{
    public async Task<IPAddress[]> GetHostAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}

internal sealed class OutboundRequestBlockedException(string message) : Exception(message);

internal sealed class OutboundAddressPolicy
{
    private readonly IHostAddressResolver _resolver;
    private readonly HashSet<string> _allowedHosts;
    private readonly IpNetworkRange[] _allowedNetworks;

    public OutboundAddressPolicy(
        IOptions<OutboundHttpOptions> options,
        IHostAddressResolver resolver)
    {
        _resolver = resolver;
        _allowedHosts = options.Value.AllowedPrivateHosts
            .Select(host => host.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(host => host.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _allowedNetworks = options.Value.AllowedPrivateNetworks
            .Select(value => IpNetworkRange.Parse(value.Trim()))
            .ToArray();
    }

    public async Task ValidateUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        ValidateUriShape(uri);
        var addresses = await ResolveAsync(uri.IdnHost, cancellationToken);
        EnsureAddressesAllowed(uri.IdnHost, addresses);
    }

    public async Task<IPAddress[]> ResolveConnectionAddressesAsync(
        DnsEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        var addresses = await ResolveAsync(endpoint.Host, cancellationToken);
        EnsureAddressesAllowed(endpoint.Host, addresses);
        return InterleaveAddressFamilies(addresses.Distinct().ToArray());
    }

    internal static void ValidateUriShape(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new OutboundRequestBlockedException("Only absolute HTTP(S) URLs are allowed.");

        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.IdnHost))
            throw new OutboundRequestBlockedException("Outbound URLs cannot contain credentials.");
    }

    internal static bool IsPubliclyRoutable(IPAddress input)
    {
        var address = input.IsIPv4MappedToIPv6 ? input.MapToIPv4() : input;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            return first switch
            {
                0 or 10 or 127 => false,
                100 when second is >= 64 and <= 127 => false,
                169 when second == 254 => false,
                172 when second is >= 16 and <= 31 => false,
                192 when second == 0 => false,
                192 when second == 168 => false,
                198 when second is 18 or 19 => false,
                198 when second == 51 && third == 100 => false,
                203 when second == 0 && third == 113 => false,
                >= 224 => false,
                _ => true
            };
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            return false;

        // Fail closed for IPv6 transition mechanisms which embed an IPv4 destination.
        // Validating only the outer IPv6 address would otherwise allow a connector or an
        // upstream NAT64/6to4/Teredo gateway to reach private IPv4 services.
        var isIpv4Compatible = bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0;
        var isNat64WellKnown = bytes[0] == 0x00 && bytes[1] == 0x64 &&
                               bytes[2] == 0xff && bytes[3] == 0x9b &&
                               bytes.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0;
        var isNat64LocalUse = bytes[0] == 0x00 && bytes[1] == 0x64 &&
                             bytes[2] == 0xff && bytes[3] == 0x9b &&
                             bytes[4] == 0x00 && bytes[5] == 0x01;
        var isTeredo = bytes[0] == 0x20 && bytes[1] == 0x01 &&
                       bytes[2] == 0x00 && bytes[3] == 0x00;
        var is6To4 = bytes[0] == 0x20 && bytes[1] == 0x02;

        // fc00::/7 (unique local), 100::/64 (discard-only), and 2001:db8::/32
        // (documentation) are never valid public feed destinations.
        if ((bytes[0] & 0xfe) == 0xfc ||
            (bytes[0] == 0x01 && bytes[1] == 0x00 && bytes.Skip(2).Take(6).All(value => value == 0)) ||
            (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) ||
            isIpv4Compatible || isNat64WellKnown || isNat64LocalUse || isTeredo || is6To4)
            return false;

        return true;
    }

    private async Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
            return [literal];

        IPAddress[] addresses;
        try
        {
            addresses = await _resolver.GetHostAddressesAsync(host, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new OutboundRequestBlockedException(
                $"The outbound host '{host}' could not be resolved: {exception.SocketErrorCode}.");
        }

        if (addresses.Length == 0)
            throw new OutboundRequestBlockedException($"The outbound host '{host}' has no addresses.");
        return addresses;
    }

    private void EnsureAddressesAllowed(string host, IReadOnlyCollection<IPAddress> addresses)
    {
        var normalizedHost = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (_allowedHosts.Contains(normalizedHost))
            return;

        var denied = addresses.FirstOrDefault(address =>
            !IsPubliclyRoutable(address) && !_allowedNetworks.Any(network => network.Contains(address)));
        if (denied is not null)
            throw new OutboundRequestBlockedException(
                $"The outbound host '{host}' resolved to a blocked network.");
    }

    private static IPAddress[] InterleaveAddressFamilies(IReadOnlyList<IPAddress> addresses)
    {
        if (addresses.Count < 2)
            return addresses.ToArray();

        var preferredFamily = addresses[0].AddressFamily;
        var preferred = new Queue<IPAddress>(addresses.Where(address =>
            address.AddressFamily == preferredFamily));
        var alternate = new Queue<IPAddress>(addresses.Where(address =>
            address.AddressFamily != preferredFamily));
        var ordered = new List<IPAddress>(addresses.Count);
        while (preferred.Count > 0 || alternate.Count > 0)
        {
            if (preferred.TryDequeue(out var preferredAddress))
                ordered.Add(preferredAddress);
            if (alternate.TryDequeue(out var alternateAddress))
                ordered.Add(alternateAddress);
        }
        return ordered.ToArray();
    }

    private readonly record struct IpNetworkRange(byte[] Network, int PrefixLength)
    {
        internal static IpNetworkRange Parse(string value)
        {
            var separator = value.LastIndexOf('/');
            var addressText = separator < 0 ? value : value[..separator];
            if (!IPAddress.TryParse(addressText, out var address))
                throw new OptionsValidationException(
                    OutboundHttpOptions.SectionName,
                    typeof(OutboundHttpOptions),
                    [$"Invalid private network allowlist entry '{value}'."]);

            address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            var maximumPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            var prefix = separator < 0
                ? maximumPrefix
                : int.TryParse(value[(separator + 1)..], out var parsed) ? parsed : -1;
            if (prefix < 0 || prefix > maximumPrefix)
                throw new OptionsValidationException(
                    OutboundHttpOptions.SectionName,
                    typeof(OutboundHttpOptions),
                    [$"Invalid private network allowlist entry '{value}'."]);

            var network = address.GetAddressBytes();
            ApplyMask(network, prefix);
            return new IpNetworkRange(network, prefix);
        }

        internal bool Contains(IPAddress input)
        {
            var address = input.IsIPv4MappedToIPv6 ? input.MapToIPv4() : input;
            var bytes = address.GetAddressBytes();
            if (bytes.Length != Network.Length)
                return false;

            var fullBytes = PrefixLength / 8;
            var remainingBits = PrefixLength % 8;
            if (!bytes.AsSpan(0, fullBytes).SequenceEqual(Network.AsSpan(0, fullBytes)))
                return false;
            if (remainingBits == 0)
                return true;

            var mask = (byte)(0xff << (8 - remainingBits));
            return (bytes[fullBytes] & mask) == (Network[fullBytes] & mask);
        }

        private static void ApplyMask(Span<byte> bytes, int prefixLength)
        {
            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;
            if (fullBytes < bytes.Length)
            {
                if (remainingBits > 0)
                {
                    bytes[fullBytes] &= (byte)(0xff << (8 - remainingBits));
                    fullBytes++;
                }
                bytes[fullBytes..].Clear();
            }
        }
    }
}
