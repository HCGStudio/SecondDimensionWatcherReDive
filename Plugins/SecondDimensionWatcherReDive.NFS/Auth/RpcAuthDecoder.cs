using SecondDimensionWatcherReDive.NFS.Rpc;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Auth;

internal static class RpcAuthDecoder
{
    public static AuthSysCred ReadCredential(ref XdrReader reader, bool allowAnonymous = false)
    {
        var flavor = reader.ReadUInt32();
        var body = reader.ReadOpaque();
        if (body.Length > RpcConstants.MaxOpaqueAuthBytes)
            throw new XdrException($"Credential body too large ({body.Length} bytes)");

        return flavor switch
        {
            RpcConstants.AuthNone when allowAnonymous => AuthSysCred.Anonymous,
            RpcConstants.AuthNone => throw new RpcAuthRejectedException("AUTH_NONE is disabled"),
            RpcConstants.AuthSys => DecodeAuthSys(body),
            _ => throw new RpcAuthRejectedException($"Unsupported credential flavor {flavor}")
        };
    }

    public static void ReadAndDiscardVerifier(ref XdrReader reader)
    {
        _ = reader.ReadUInt32();
        var body = reader.ReadOpaque();
        if (body.Length > RpcConstants.MaxOpaqueAuthBytes)
            throw new XdrException($"Verifier body too large ({body.Length} bytes)");
    }

    private static AuthSysCred DecodeAuthSys(ReadOnlySpan<byte> body)
    {
        var inner = new XdrReader(body);
        var stamp = inner.ReadUInt32();
        var machineNameBytes = inner.ReadOpaque();
        if (machineNameBytes.Length > RpcConstants.MaxMachineNameBytes)
            throw new XdrException($"AUTH_SYS machinename too long ({machineNameBytes.Length} bytes)");
        var machineName = machineNameBytes.IsEmpty
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(machineNameBytes);
        var uid = inner.ReadUInt32();
        var gid = inner.ReadUInt32();
        var gidsCount = inner.ReadUInt32();
        if (gidsCount > 16)
            throw new XdrException($"AUTH_SYS gids array too long ({gidsCount})");
        var gids = new uint[gidsCount];
        for (var i = 0; i < gidsCount; i++)
            gids[i] = inner.ReadUInt32();
        return new AuthSysCred(stamp, machineName, uid, gid, gids);
    }
}

internal sealed class RpcAuthRejectedException(string message) : Exception(message);
