using SecondDimensionWatcherReDive.NFS.Auth;
using SecondDimensionWatcherReDive.NFS.Protocol;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed class NfsRequestContext
{
    public NfsFileHandle? CurrentFh { get; set; }
    public NfsFileHandle? SavedFh { get; set; }
    public AuthSysCred Credential { get; init; } = AuthSysCred.Anonymous;
    public CancellationToken CancellationToken { get; init; }
}
