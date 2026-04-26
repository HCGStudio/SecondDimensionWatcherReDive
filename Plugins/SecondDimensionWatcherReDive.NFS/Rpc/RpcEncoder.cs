using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Rpc;

internal static class RpcEncoder
{
    public static void WriteAcceptedSuccessHeader(XdrWriter writer, uint xid)
    {
        writer.WriteUInt32(xid);
        writer.WriteUInt32(RpcConstants.Reply);
        writer.WriteUInt32(RpcConstants.MsgAccepted);
        WriteOpaqueAuthNone(writer);
        writer.WriteUInt32(RpcConstants.Success);
    }

    public static void WriteAcceptedErrorHeader(XdrWriter writer, uint xid, uint acceptStatus)
    {
        writer.WriteUInt32(xid);
        writer.WriteUInt32(RpcConstants.Reply);
        writer.WriteUInt32(RpcConstants.MsgAccepted);
        WriteOpaqueAuthNone(writer);
        writer.WriteUInt32(acceptStatus);
    }

    public static void WriteProgramMismatchHeader(
        XdrWriter writer,
        uint xid,
        uint lowVersion,
        uint highVersion)
    {
        WriteAcceptedErrorHeader(writer, xid, RpcConstants.ProgMismatch);
        writer.WriteUInt32(lowVersion);
        writer.WriteUInt32(highVersion);
    }

    public static void WriteAuthErrorHeader(XdrWriter writer, uint xid, uint authStatus)
    {
        writer.WriteUInt32(xid);
        writer.WriteUInt32(RpcConstants.Reply);
        writer.WriteUInt32(RpcConstants.MsgDenied);
        writer.WriteUInt32(RpcConstants.AuthError);
        writer.WriteUInt32(authStatus);
    }

    public static void WriteRpcMismatchHeader(
        XdrWriter writer,
        uint xid,
        uint lowVersion,
        uint highVersion)
    {
        writer.WriteUInt32(xid);
        writer.WriteUInt32(RpcConstants.Reply);
        writer.WriteUInt32(RpcConstants.MsgDenied);
        writer.WriteUInt32(RpcConstants.RpcMismatch);
        writer.WriteUInt32(lowVersion);
        writer.WriteUInt32(highVersion);
    }

    private static void WriteOpaqueAuthNone(XdrWriter writer)
    {
        writer.WriteUInt32(RpcConstants.AuthNone);
        writer.WriteUInt32(0);
    }
}
