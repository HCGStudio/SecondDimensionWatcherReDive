namespace SecondDimensionWatcherReDive.NFS.Auth;

internal sealed record AuthSysCred(
    uint Stamp,
    string MachineName,
    uint Uid,
    uint Gid,
    uint[] Gids)
{
    public static AuthSysCred Anonymous { get; } = new(0, string.Empty, 0, 0, []);
}
