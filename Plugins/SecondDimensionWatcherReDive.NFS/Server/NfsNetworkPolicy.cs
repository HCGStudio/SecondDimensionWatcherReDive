using System.Net;
using SecondDimensionWatcherReDive.Framework.Networking;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed class NfsNetworkPolicy
{
    private readonly IpCidrRange[] _networks;

    public NfsNetworkPolicy(IEnumerable<string> networks)
    {
        _networks = networks.Select(ParseNetwork).ToArray();
        if (_networks.Length == 0)
            throw new InvalidOperationException("Nfs:AllowedNetworks must contain at least one CIDR.");
    }

    public bool IsAllowed(IPAddress input) => _networks.Any(network => network.Contains(input));

    private static IpCidrRange ParseNetwork(string value)
    {
        if (IpCidrRange.TryParse(value, requirePrefix: false, out var network))
            return network;
        throw new InvalidOperationException($"Invalid Nfs:AllowedNetworks CIDR '{value}'.");
    }
}
