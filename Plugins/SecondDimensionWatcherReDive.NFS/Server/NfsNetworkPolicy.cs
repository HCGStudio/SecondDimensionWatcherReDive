using System.Net;
using System.Net.Sockets;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed class NfsNetworkPolicy
{
    private readonly NetworkRange[] _networks;

    public NfsNetworkPolicy(IEnumerable<string> networks)
    {
        _networks = networks.Select(NetworkRange.Parse).ToArray();
        if (_networks.Length == 0)
            throw new InvalidOperationException("Nfs:AllowedNetworks must contain at least one CIDR.");
    }

    public bool IsAllowed(IPAddress input) => _networks.Any(network => network.Contains(input));

    private readonly record struct NetworkRange(byte[] Network, int PrefixLength)
    {
        internal static NetworkRange Parse(string value)
        {
            var trimmed = value.Trim();
            var separator = trimmed.LastIndexOf('/');
            var addressPart = separator < 0 ? trimmed : trimmed[..separator];
            if (!IPAddress.TryParse(addressPart, out var address))
                throw new InvalidOperationException($"Invalid Nfs:AllowedNetworks CIDR '{value}'.");
            address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

            var maximumPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            var prefix = separator < 0
                ? maximumPrefix
                : int.TryParse(trimmed[(separator + 1)..], out var parsed) ? parsed : -1;
            if (prefix < 0 || prefix > maximumPrefix)
                throw new InvalidOperationException($"Invalid Nfs:AllowedNetworks CIDR '{value}'.");

            var network = address.GetAddressBytes();
            var fullBytes = prefix / 8;
            var remainingBits = prefix % 8;
            if (fullBytes < network.Length)
            {
                if (remainingBits > 0)
                {
                    network[fullBytes] &= (byte)(0xff << (8 - remainingBits));
                    fullBytes++;
                }
                network.AsSpan(fullBytes).Clear();
            }
            return new NetworkRange(network, prefix);
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
    }
}
